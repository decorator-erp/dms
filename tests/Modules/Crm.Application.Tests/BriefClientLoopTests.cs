using Approvals.Contracts;
using Approvals.Contracts.Approvals;
using BuildingBlocks.Messaging;
using Crm.Application.Features.Briefs;
using Crm.Contracts.Briefs;
using Crm.Contracts.Portal;
using Crm.Domain;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using SharedKernel;
using Xunit;
using ApprovalDecision = Approvals.Contracts.ApprovalDecision;
using CrmApprovalState = Crm.Contracts.CrmApprovalState;
using CrmBriefHeadData = Crm.Contracts.Briefs.CrmBriefHeadData;
using UnitCondition = Crm.Contracts.UnitCondition;
using UnitType = Crm.Contracts.UnitType;

namespace Crm.Application.Tests;

/// <summary>
/// Run C of the CRM brief fixes (M5): the client's view of a brief is what they were last sent, without
/// the office's budget, and the round diff exists for both audiences — the client's never carrying
/// <c>ExpectedBudget</c>. Client decisions here go through the <see cref="DecideApprovalCommand"/> handler; the
/// client's portal decide (B1) is covered by <c>PortalApprovalTests</c>.
/// </summary>
[Collection(CrmApplicationCollection.Name)]
public sealed class BriefClientLoopTests(CrmApplicationFixture fixture)
{
    private static readonly CrmBriefHeadData Head = new(
        (UnitType)1, (UnitCondition)1, Address: "First Addr", ExpectedBudget: 50_000m);

    [Fact]
    public async Task PortalView_HeadIsNullUntilSent_ThenIsTheSentRoundWithoutBudget_AndHidesDraftEntries()
    {
        BriefTestProject p = await SeedAsync("portal-view");
        Guid briefId = await CreateBriefAsync(p);
        Guid sentEntry = await CreateEntryAsync(p, briefId, "sent");
        await CreateEntryAsync(p, briefId, "still a draft");

        PortalBriefViewResponse before = (await PortalViewAsync(p)).Value!;
        Assert.Null(before.Head);
        Assert.Empty(before.Spaces);

        Assert.True((await AsOwner<SendCrmBriefCommand>(p, new SendCrmBriefCommand(briefId, p.OwnerAppUserId))).IsSuccess);
        Assert.True((await AsOwner<SendCrmBriefSpaceCommand>(p, new SendCrmBriefSpaceCommand(sentEntry, p.OwnerAppUserId))).IsSuccess);

        PortalBriefViewResponse after = (await PortalViewAsync(p)).Value!;
        Assert.NotNull(after.Head);
        Assert.Equal(briefId, after.Head.BriefId);
        Assert.Equal(1, after.Head.Round);
        Assert.Equal("First Addr", after.Head.Address);
        Assert.Equal(CrmApprovalState.PendingApproval, after.Head.Status);
        Assert.Equal(sentEntry, Assert.Single(after.Spaces).Id);
        Assert.Null(typeof(BriefHeadBlock).GetProperty(nameof(CrmBriefHeadData.ExpectedBudget)));
        Assert.Null(typeof(BriefHeadBlock).GetProperty(nameof(CrmDesignBriefResponse.LastMeetingId)));
    }

    [Fact]
    public async Task RoundDiff_OfficeSeesTheBudgetChange_ClientNeverDoes_ClientDecidesViaHandler_NotPortalRoute()
    {
        BriefTestProject p = await SeedAsync("diff-audience");
        Guid briefId = await CreateBriefAsync(p);
        Assert.True((await AsOwner<SendCrmBriefCommand>(p, new SendCrmBriefCommand(briefId, p.OwnerAppUserId))).IsSuccess);
        await DecideAsClientAsync(p, CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, briefId, 1), ApprovalDecision.Rejected);

        Assert.True((await AsOwner<UpdateCrmBriefHeadCommand>(p, new UpdateCrmBriefHeadCommand(
            briefId, Head with { Address = "Second Addr", ExpectedBudget = 80_000m }, p.OwnerAppUserId))).IsSuccess);
        Assert.True((await AsOwner<SendCrmBriefCommand>(p, new SendCrmBriefCommand(briefId, p.OwnerAppUserId))).IsSuccess);

        BriefHeadDiffResponse office = (await OfficeDiffAsync(p.OwnerAppUserId, briefId, 2)).Value!;
        BriefHeadDiffResponse client = (await ClientDiffAsync(p.ClientAppUserId, briefId, 2)).Value!;
        BriefHeadDiffResponse clientRoundOne = (await ClientDiffAsync(p.ClientAppUserId, briefId, 1)).Value!;

        Assert.Equal(1, office.PreviousRound);
        BriefFieldChangeResponse budget = Assert.Single(office.Changes, c => c.Field == nameof(CrmBriefHeadData.ExpectedBudget));
        Assert.Equal("50000", budget.PreviousValue);
        Assert.Equal("80000", budget.CurrentValue);
        Assert.Contains(office.Changes, c => c.Field == nameof(CrmBriefHeadData.Address));

        BriefFieldChangeResponse address = Assert.Single(client.Changes);
        Assert.Equal(("First Addr", "Second Addr"), (address.PreviousValue, address.CurrentValue));
        Assert.Null(clientRoundOne.PreviousRound);
        Assert.DoesNotContain(clientRoundOne.Changes, c => c.Field == nameof(CrmBriefHeadData.ExpectedBudget));
        Assert.Contains(clientRoundOne.Changes, c => c.Field == nameof(CrmBriefHeadData.Address) && c.PreviousValue == null);

        Result<BriefHeadDiffResponse> missingRound = await OfficeDiffAsync(p.OwnerAppUserId, briefId, 3);
        Assert.Equal(CrmBriefErrors.RoundNotFound, missingRound.Error);
        Assert.Equal(404, missingRound.Error.Status);
    }

    [Fact]
    public async Task RoundDiff_OnAnotherProjectsBrief_Is404_ForBothAudiences()
    {
        BriefTestProject p = await SeedAsync("diff-scope");
        BriefTestProject other = await SeedAsync("diff-scope-other");
        Guid briefId = await CreateBriefAsync(p);
        Assert.True((await AsOwner<SendCrmBriefCommand>(p, new SendCrmBriefCommand(briefId, p.OwnerAppUserId))).IsSuccess);
        string outsiderEmployee;
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            outsiderEmployee = (await HrTestSeeding.HireEmployeeAsync(
                scope.ServiceProvider, $"diff-outsider-{Guid.NewGuid():N}@example.com", jobTitle: "Designer", firstName: "Out")).AppUserId;
        }

        Result<BriefHeadDiffResponse> otherClient = await ClientDiffAsync(other.ClientAppUserId, briefId, 1);
        Result<BriefHeadDiffResponse> office = await OfficeDiffAsync(outsiderEmployee, briefId, 1, readAll: false);

        Assert.Equal(404, otherClient.Error.Status);
        Assert.Equal(404, office.Error.Status);
        Assert.True((await ClientDiffAsync(p.ClientAppUserId, briefId, 1)).IsSuccess);
    }

    private async Task<BriefTestProject> SeedAsync(string label)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await BriefTestSeeding.CreateProjectWithStageAsync(scope.ServiceProvider, label);
    }

    private AsyncServiceScope ScopeFor(string userId, bool readAll)
    {
        AsyncServiceScope scope = fixture.CreateScope();
        TestCurrentUser currentUser = scope.ServiceProvider.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = userId;
        if (readAll)
        {
            currentUser.Grant(ProjectsPermissions.Projects.ReadAll);
        }

        return scope;
    }

    private async Task<Result> AsOwner<TCommand>(BriefTestProject p, TCommand command) where TCommand : ICommand
    {
        await using AsyncServiceScope scope = ScopeFor(p.OwnerAppUserId, readAll: true);
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand>>().HandleAsync(command);
    }

    private async Task<Guid> CreateBriefAsync(BriefTestProject p)
    {
        await using AsyncServiceScope scope = ScopeFor(p.OwnerAppUserId, readAll: true);
        Result<Guid> result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateCrmDesignBriefCommand, Guid>>()
            .HandleAsync(new CreateCrmDesignBriefCommand(p.ProjectId, p.EmployeeId, p.StageId, Head, p.OwnerAppUserId));
        Assert.True(result.IsSuccess, result.Error?.Description);
        return result.Value;
    }

    private async Task<Guid> CreateEntryAsync(BriefTestProject p, Guid briefId, string requirements)
    {
        await using AsyncServiceScope scope = ScopeFor(p.OwnerAppUserId, readAll: true);
        Result<Guid> result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateCrmBriefSpaceCommand, Guid>>()
            .HandleAsync(new CreateCrmBriefSpaceCommand(briefId, p.EmployeeId, p.OwnerAppUserId, SpaceId: null, Requirements: requirements));
        Assert.True(result.IsSuccess, result.Error?.Description);
        return result.Value;
    }

    private async Task<Result<PortalBriefViewResponse>> PortalViewAsync(BriefTestProject p)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetMyPortalBriefQuery, PortalBriefViewResponse>>()
            .HandleAsync(new GetMyPortalBriefQuery(p.ClientAppUserId, p.ProjectId, p.StageId));
    }

    private async Task<Result<BriefHeadDiffResponse>> OfficeDiffAsync(string userId, Guid briefId, int round, bool readAll = true)
    {
        await using AsyncServiceScope scope = ScopeFor(userId, readAll);
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetCrmBriefRoundDiffQuery, BriefHeadDiffResponse>>()
            .HandleAsync(new GetCrmBriefRoundDiffQuery(briefId, round, userId));
    }

    private async Task<Result<BriefHeadDiffResponse>> ClientDiffAsync(string clientAppUserId, Guid briefId, int round)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetMyPortalBriefRoundDiffQuery, BriefHeadDiffResponse>>()
            .HandleAsync(new GetMyPortalBriefRoundDiffQuery(clientAppUserId, briefId, round));
    }

    private async Task DecideAsClientAsync(BriefTestProject p, Guid approvalId, ApprovalDecision decision)
    {
        await using (AsyncServiceScope scope = ScopeFor(p.ClientAppUserId, readAll: false))
        {
            Result decided = await scope.ServiceProvider.GetRequiredService<ICommandHandler<DecideApprovalCommand>>()
                .HandleAsync(new DecideApprovalCommand(approvalId, decision, "Needs rework"));
            Assert.True(decided.IsSuccess, decided.Error?.Description);
        }

        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);
    }
}
