using Approvals.Contracts;
using Approvals.Contracts.Approvals;
using BuildingBlocks.Messaging;
using Crm.Application.Features.Briefs;
using Crm.Contracts.Briefs;
using Crm.Domain;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using SharedKernel;
using Xunit;
using ApprovalDecision = Approvals.Contracts.ApprovalDecision;
using CrmBriefHeadData = Crm.Contracts.Briefs.CrmBriefHeadData;
using HiringReason = Crm.Contracts.HiringReason;
using UnitCondition = Crm.Contracts.UnitCondition;
using UnitType = Crm.Contracts.UnitType;

namespace Crm.Application.Tests;

/// <summary>Run D of the CRM brief fixes: validators that match the columns (M4), honest create errors
/// (L1), a 404 for an unknown brief's entries and the ledger's <c>IsCurrent</c> flag (L3).</summary>
[Collection(CrmApplicationCollection.Name)]
public sealed class BriefHardeningTests(CrmApplicationFixture fixture)
{
    private static readonly CrmBriefHeadData Head = new((UnitType)1, (UnitCondition)1);

    public static TheoryData<string, CrmBriefHeadData> InvalidHeads => new()
    {
        { "Data.Address", Head with { Address = new string('a', 513) } },
        { "Data.OtherNeeds", Head with { OtherNeeds = new string('a', 2001) } },
        { "Data.PreferredStyle", Head with { PreferredStyle = new string('a', 257) } },
        { "Data.AreaSqm", Head with { AreaSqm = 0m } },
        { "Data.FloorNo", Head with { FloorNo = -1 } },
        { "Data.UnitsPerFloor", Head with { UnitsPerFloor = 0 } },
        { "Data.ExpectedBudget", Head with { ExpectedBudget = -1m } },
        { "Data.UnitType", Head with { UnitType = (UnitType)99 } },
        { "Data.HiringReasons", Head with { HiringReasons = (HiringReason)64 } },
    };

    [Theory]
    [MemberData(nameof(InvalidHeads))]
    public async Task CreateBrief_WithAnInvalidHeadField_Is400_NotA500(string property, CrmBriefHeadData data)
    {
        BriefTestProject p = await SeedAsync("invalid-head");

        Result<Guid> result = await CreateBriefAsync(p, data);

        Assert.Equal(400, result.Error.Status);
        ValidationError validation = Assert.IsType<ValidationError>(result.Error);
        Assert.Contains(validation.Errors, e => e.Code == property);
    }

    [Fact]
    public async Task UpdateHead_WithAnOverLongField_Is400()
    {
        BriefTestProject p = await SeedAsync("invalid-update");
        Guid briefId = (await CreateBriefAsync(p, Head)).Value;

        Result result = await AsOwner(p, sp => sp.GetRequiredService<ICommandHandler<UpdateCrmBriefHeadCommand>>()
            .HandleAsync(new UpdateCrmBriefHeadCommand(briefId, Head with { VisitorsNote = new string('a', 1001) }, p.OwnerAppUserId)));

        Assert.Equal(400, result.Error.Status);
    }

    [Fact]
    public async Task Entry_WithOverLongRequirements_OrNoPieces_Is400()
    {
        BriefTestProject p = await SeedAsync("invalid-entry");
        Guid briefId = (await CreateBriefAsync(p, Head)).Value;

        Result<Guid> tooLong = await CreateEntryAsync(p, briefId, new string('a', 2001), countOrPieces: null);
        Result<Guid> zeroPieces = await CreateEntryAsync(p, briefId, "ok", countOrPieces: 0);

        Assert.Equal(400, tooLong.Error.Status);
        Assert.Equal(400, zeroPieces.Error.Status);
        Assert.True((await CreateEntryAsync(p, briefId, new string('a', 2000), countOrPieces: 1)).IsSuccess);
    }

    [Fact]
    public async Task DuplicateCreate_IsAlreadyExists_AndAnUnknownProject_IsProjectNotFound()
    {
        BriefTestProject p = await SeedAsync("create-errors");
        Assert.True((await CreateBriefAsync(p, Head)).IsSuccess);

        Result<Guid> duplicate = await CreateBriefAsync(p, Head);
        Result<Guid> unknownProject = await AsOwner(p, sp => sp.GetRequiredService<ICommandHandler<CreateCrmDesignBriefCommand, Guid>>()
            .HandleAsync(new CreateCrmDesignBriefCommand(Guid.NewGuid(), p.EmployeeId, p.StageId, Head, p.OwnerAppUserId)));

        Assert.Equal(CrmBriefErrors.AlreadyExists, duplicate.Error);
        Assert.Equal(409, duplicate.Error.Status);
        Assert.Equal(CrmBriefErrors.ProjectNotFound, unknownProject.Error);
        Assert.Equal(404, unknownProject.Error.Status);
    }

    [Fact]
    public async Task SpacesList_ForAnUnknownBrief_Is404_AndFlagsTheCurrentLedgerLine()
    {
        BriefTestProject p = await SeedAsync("spaces-current");
        Result<IReadOnlyList<CrmBriefSpaceResponse>> unknown = await ListAsync(p, Guid.NewGuid());
        Assert.Equal(404, unknown.Error.Status);

        Guid briefId = (await CreateBriefAsync(p, Head)).Value;
        Guid original = (await CreateEntryAsync(p, briefId, "v1", countOrPieces: 1)).Value;
        Assert.True((await AsOwner(p, sp => sp.GetRequiredService<ICommandHandler<SendCrmBriefSpaceCommand>>()
            .HandleAsync(new SendCrmBriefSpaceCommand(original, p.OwnerAppUserId)))).IsSuccess);
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TestCurrentUser>().UserId = p.ClientAppUserId;
            Assert.True((await scope.ServiceProvider.GetRequiredService<ICommandHandler<DecideApprovalCommand>>()
                .HandleAsync(new DecideApprovalCommand(
                    CrmApprovalIds.For(ApprovalSubjectType.BriefSpace, original, 1), ApprovalDecision.Approved, null))).IsSuccess);
        }

        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);
        Guid correction = (await AsOwner(p, sp => sp.GetRequiredService<ICommandHandler<SupersedeCrmBriefSpaceCommand, Guid>>()
            .HandleAsync(new SupersedeCrmBriefSpaceCommand(original, p.EmployeeId, p.OwnerAppUserId, SpaceId: null, Requirements: "v2")))).Value;

        IReadOnlyList<CrmBriefSpaceResponse> entries = (await ListAsync(p, briefId)).Value!;

        Assert.False(entries.Single(e => e.Id == original).IsCurrent);
        Assert.True(entries.Single(e => e.Id == correction).IsCurrent);
    }

    private async Task<BriefTestProject> SeedAsync(string label)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await BriefTestSeeding.CreateProjectWithStageAsync(scope.ServiceProvider, label);
    }

    private async Task<T> AsOwner<T>(BriefTestProject p, Func<IServiceProvider, Task<T>> action)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        TestCurrentUser currentUser = scope.ServiceProvider.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = p.OwnerAppUserId;
        currentUser.Grant(ProjectsPermissions.Projects.ReadAll);
        return await action(scope.ServiceProvider);
    }

    private Task<Result<Guid>> CreateBriefAsync(BriefTestProject p, CrmBriefHeadData data) =>
        AsOwner(p, sp => sp.GetRequiredService<ICommandHandler<CreateCrmDesignBriefCommand, Guid>>()
            .HandleAsync(new CreateCrmDesignBriefCommand(p.ProjectId, p.EmployeeId, p.StageId, data, p.OwnerAppUserId)));

    private Task<Result<Guid>> CreateEntryAsync(BriefTestProject p, Guid briefId, string requirements, int? countOrPieces) =>
        AsOwner(p, sp => sp.GetRequiredService<ICommandHandler<CreateCrmBriefSpaceCommand, Guid>>()
            .HandleAsync(new CreateCrmBriefSpaceCommand(
                briefId, p.EmployeeId, p.OwnerAppUserId, SpaceId: null, CountOrPieces: countOrPieces, Requirements: requirements)));

    private Task<Result<IReadOnlyList<CrmBriefSpaceResponse>>> ListAsync(BriefTestProject p, Guid briefId) =>
        AsOwner(p, sp => sp.GetRequiredService<IQueryHandler<GetCrmBriefSpacesByBriefQuery, IReadOnlyList<CrmBriefSpaceResponse>>>()
            .HandleAsync(new GetCrmBriefSpacesByBriefQuery(briefId, p.OwnerAppUserId)));
}
