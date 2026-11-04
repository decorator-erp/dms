using Approvals.Contracts;
using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Briefs;
using Crm.Contracts.Items;
using Crm.Contracts.Periods;
using Crm.Contracts.Portal;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using SharedKernel;
using Xunit;
using ApprovalState = Crm.Domain.ApprovalState;
using ApprovalType = Approvals.Contracts.ApprovalType;
using CrmBriefHeadData = Crm.Contracts.Briefs.CrmBriefHeadData;
using RequirementCategory = Crm.Contracts.RequirementCategory;
using RequirementPriority = Crm.Contracts.RequirementPriority;
using UnitCondition = Crm.Contracts.UnitCondition;
using UnitType = Crm.Contracts.UnitType;

namespace Crm.Application.Tests;

/// <summary>
/// B1.2 / B1.5 (Option A): the client decides its own approvals in the portal, through
/// <see cref="DecideMyPortalApprovalCommand"/> — no office permission, no office handler. The subject reacts
/// from the outbox (drained twice: Approvals' decided event → CRM, then CRM's own events).
/// </summary>
[Collection(CrmApplicationCollection.Name)]
public sealed partial class PortalApprovalTests(CrmApplicationFixture fixture)
{
    private static readonly CrmBriefHeadData Head = new((UnitType)1, (UnitCondition)1, Address: "Portal Addr");

    [Fact]
    public async Task Client_ApprovesABriefRound_InThePortal_BriefIsApproved_AndTheBriefGateIsSatisfied()
    {
        BriefTestProject p = await SeedAsync("portal-approve");
        Guid briefId = await CreateBriefAsync(p);
        Assert.True((await AsOwner(p, new SendCrmBriefCommand(briefId, p.OwnerAppUserId))).IsSuccess);
        Guid approvalId = (await LoadBriefAsync(briefId)).PendingApprovalId!.Value;

        Result result = await DecideAsync(p.ClientAppUserId, approvalId, PortalApprovalDecision.Approved);
        Assert.True(result.IsSuccess, result.Error?.Description);
        await DrainOutboxAsync();

        CrmDesignBrief brief = await LoadBriefAsync(briefId);
        Assert.Equal(ApprovalState.Approved, brief.Status);
        Assert.Null(brief.PendingApprovalId);
        await using AsyncServiceScope scope = fixture.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<ICrmGateReader>()
            .IsBriefApprovedForStageAsync(p.StageId, CancellationToken.None));
    }

    [Fact]
    public async Task Client_RequestsChanges_WithAComment_BriefIsRejected()
    {
        BriefTestProject p = await SeedAsync("portal-changes");
        Guid briefId = await CreateBriefAsync(p);
        Assert.True((await AsOwner(p, new SendCrmBriefCommand(briefId, p.OwnerAppUserId))).IsSuccess);
        Guid approvalId = (await LoadBriefAsync(briefId)).PendingApprovalId!.Value;

        Assert.True((await DecideAsync(p.ClientAppUserId, approvalId, PortalApprovalDecision.ChangesRequested, "Move the door")).IsSuccess);
        await DrainOutboxAsync();

        Assert.Equal(ApprovalState.Rejected, (await LoadBriefAsync(briefId)).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task ChangesRequested_WithNoComment_Is400(string? comment)
    {
        (BriefTestProject p, Guid approvalId) = await SentBriefAsync("portal-nocomment");

        Result result = await DecideAsync(p.ClientAppUserId, approvalId, PortalApprovalDecision.ChangesRequested, comment);

        AssertError(result, 400, "approvals.CommentRequired");
    }

    [Theory]
    [InlineData(PortalApprovalDecision.Rejected)]
    [InlineData((PortalApprovalDecision)99)]
    public async Task ADecisionOtherThanApprovedOrChangesRequested_Is400(PortalApprovalDecision decision)
    {
        (BriefTestProject p, Guid approvalId) = await SentBriefAsync("portal-baddecision");

        Result result = await DecideAsync(p.ClientAppUserId, approvalId, decision, "No");

        AssertError(result, 400, "crm.portal.InvalidDecision");
    }

    [Fact]
    public async Task AnotherClientsApproval_Is404_AndStaysPending()
    {
        (BriefTestProject p, Guid approvalId) = await SentBriefAsync("portal-mine");
        BriefTestProject other = await SeedAsync("portal-theirs");

        Result result = await DecideAsync(other.ClientAppUserId, approvalId, PortalApprovalDecision.Approved);

        AssertError(result, 404, "crm.portal.NotFound");
        Assert.Null((await ApprovalAsync(approvalId)).Decision);
    }

    [Fact]
    public async Task UnknownApproval_Is404()
    {
        BriefTestProject p = await SeedAsync("portal-unknown");

        AssertError(await DecideAsync(p.ClientAppUserId, Guid.NewGuid(), PortalApprovalDecision.Approved), 404, "crm.portal.NotFound");
    }

    /// <summary>The subject is the caller's own brief, but the approver entry is Employee-kind: Approvals'
    /// client check refuses it, and the portal says 404, not 403.</summary>
    [Fact]
    public async Task AnEmployeeKindApproverApproval_OnTheClientsOwnBrief_Is404()
    {
        BriefTestProject p = await SeedAsync("portal-employee-approver");
        Guid briefId = await CreateBriefAsync(p);
        Guid approvalId = await RequestDirectlyAsync(
            p, ApprovalSubjectType.DesignBrief, briefId, ApproverKind.Employee, p.ClientAppUserId);

        Result result = await DecideAsync(p.ClientAppUserId, approvalId, PortalApprovalDecision.Approved);

        AssertError(result, 404, "crm.portal.NotFound");
        Assert.Null((await ApprovalAsync(approvalId)).Decision);
    }

    /// <summary>A gate-condition approval naming the client as a Client approver is still not a CRM subject.</summary>
    [Fact]
    public async Task AGateConditionApproval_Is404_EvenWhenItNamesTheClient()
    {
        BriefTestProject p = await SeedAsync("portal-gate");
        Guid approvalId = await RequestDirectlyAsync(
            p, ApprovalSubjectType.GateCondition, Guid.NewGuid(), ApproverKind.Client, p.ClientAppUserId);

        AssertError(await DecideAsync(p.ClientAppUserId, approvalId, PortalApprovalDecision.Approved), 404, "crm.portal.NotFound");
    }

    [Fact]
    public async Task AlreadyDecided_Is409()
    {
        (BriefTestProject p, Guid approvalId) = await SentBriefAsync("portal-twice");
        Assert.True((await DecideAsync(p.ClientAppUserId, approvalId, PortalApprovalDecision.Approved)).IsSuccess);

        Result second = await DecideAsync(p.ClientAppUserId, approvalId, PortalApprovalDecision.ChangesRequested, "Wait");

        AssertError(second, 409, "approvals.AlreadyDecided");
    }

    [Fact]
    public async Task ParallelDoubleDecide_ExactlyOneSucceeds_TheOtherIs409()
    {
        (BriefTestProject p, Guid approvalId) = await SentBriefAsync("portal-race");

        Result[] results = await Task.WhenAll(
            DecideInNewScopeAsync(p.ClientAppUserId, approvalId, PortalApprovalDecision.Approved, null),
            DecideInNewScopeAsync(p.ClientAppUserId, approvalId, PortalApprovalDecision.ChangesRequested, "Change"));

        Assert.Single(results, r => r.IsSuccess);
        Assert.Equal(409, Assert.Single(results, r => r.IsFailure).Error.Status);
    }

    [Fact]
    public async Task Client_ApprovesABriefSpaceEntry_InThePortal_EntryIsApproved()
    {
        BriefTestProject p = await SeedAsync("portal-entry");
        Guid briefId = await CreateBriefAsync(p);
        Guid entryId = await CreateEntryAsync(p, briefId);
        Assert.True((await AsOwner(p, new SendCrmBriefSpaceCommand(entryId, p.OwnerAppUserId))).IsSuccess);
        Guid approvalId = (await LoadEntryAsync(entryId)).PendingApprovalId!.Value;

        Assert.True((await DecideAsync(p.ClientAppUserId, approvalId, PortalApprovalDecision.Approved)).IsSuccess);
        await DrainOutboxAsync();

        Assert.Equal(ApprovalState.Approved, (await LoadEntryAsync(entryId)).ApprovalStatus);
    }

    [Fact]
    public async Task Client_RequestsChangesOnARequirementItem_InThePortal_ItemIsRejected()
    {
        BriefTestProject p = await SeedAsync("portal-item");
        Guid itemId = await CreateSentItemAsync(p);
        Guid approvalId = (await LoadItemAsync(itemId)).PendingApprovalId!.Value;

        Assert.True((await DecideAsync(p.ClientAppUserId, approvalId, PortalApprovalDecision.ChangesRequested, "Other tile")).IsSuccess);
        await DrainOutboxAsync();

        Assert.Equal(ApprovalState.Rejected, (await LoadItemAsync(itemId)).ApprovalStatus);
    }

    [Fact]
    public async Task Client_ApprovesARequirementItem_InThePortal_ItemIsApproved()
    {
        BriefTestProject p = await SeedAsync("portal-item-ok");
        Guid itemId = await CreateSentItemAsync(p);
        Guid approvalId = (await LoadItemAsync(itemId)).PendingApprovalId!.Value;

        Assert.True((await DecideAsync(p.ClientAppUserId, approvalId, PortalApprovalDecision.Approved)).IsSuccess);
        await DrainOutboxAsync();

        Assert.Equal(ApprovalState.Approved, (await LoadItemAsync(itemId)).ApprovalStatus);
    }

    private async Task<(BriefTestProject Project, Guid ApprovalId)> SentBriefAsync(string label)
    {
        BriefTestProject p = await SeedAsync(label);
        Guid briefId = await CreateBriefAsync(p);
        Assert.True((await AsOwner(p, new SendCrmBriefCommand(briefId, p.OwnerAppUserId))).IsSuccess);
        return (p, (await LoadBriefAsync(briefId)).PendingApprovalId!.Value);
    }

    private async Task<BriefTestProject> SeedAsync(string label)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await BriefTestSeeding.CreateProjectWithStageAsync(scope.ServiceProvider, label);
    }

    private AsyncServiceScope OwnerScope(BriefTestProject p)
    {
        AsyncServiceScope scope = fixture.CreateScope();
        TestCurrentUser currentUser = scope.ServiceProvider.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = p.OwnerAppUserId;
        currentUser.Grant(ProjectsPermissions.Projects.ReadAll);
        return scope;
    }

    private async Task<Result> AsOwner<TCommand>(BriefTestProject p, TCommand command) where TCommand : ICommand
    {
        await using AsyncServiceScope scope = OwnerScope(p);
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand>>().HandleAsync(command);
    }

    private async Task<TResponse> AsOwnerFor<TCommand, TResponse>(BriefTestProject p, TCommand command)
        where TCommand : ICommand<TResponse>
    {
        await using AsyncServiceScope scope = OwnerScope(p);
        Result<TResponse> result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, TResponse>>()
            .HandleAsync(command);
        Assert.True(result.IsSuccess, result.Error?.Description);
        return result.Value!;
    }

    private Task<Guid> CreateBriefAsync(BriefTestProject p) =>
        AsOwnerFor<CreateCrmDesignBriefCommand, Guid>(
            p, new CreateCrmDesignBriefCommand(p.ProjectId, p.EmployeeId, p.StageId, Head, p.OwnerAppUserId));

    private Task<Guid> CreateEntryAsync(BriefTestProject p, Guid briefId) =>
        AsOwnerFor<CreateCrmBriefSpaceCommand, Guid>(
            p, new CreateCrmBriefSpaceCommand(briefId, p.EmployeeId, p.OwnerAppUserId, SpaceId: null, Requirements: "Open plan"));

    private async Task<Guid> CreateSentItemAsync(BriefTestProject p)
    {
        Guid periodId = await AsOwnerFor<OpenCrmRequirementPeriodCommand, Guid>(
            p, new OpenCrmRequirementPeriodCommand(p.ProjectId, null, DateTime.UtcNow.AddDays(7), p.EmployeeId, p.OwnerAppUserId));
        Guid itemId = await AsOwnerFor<CreateCrmRequirementItemCommand, Guid>(
            p, new CreateCrmRequirementItemCommand(
                p.ProjectId, periodId, p.EmployeeId, RequirementCategory.Materials, "Matte flooring",
                RequirementPriority.High, p.OwnerAppUserId));
        Assert.True((await AsOwner(p, new SendCrmRequirementItemCommand(itemId, p.OwnerAppUserId))).IsSuccess);
        return itemId;
    }

    private async Task<Guid> RequestDirectlyAsync(
        BriefTestProject p, ApprovalSubjectType subjectType, Guid subjectId, ApproverKind kind, string approverUserId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        Result<ApprovalRequestOutcome> requested = await scope.ServiceProvider.GetRequiredService<IApprovalRequester>()
            .RequestAsync(
                new ApprovalRequest(
                    Guid.NewGuid(), subjectType, subjectId, ApprovalType.ClientReview, p.OwnerAppUserId,
                    [new ApproverSpec(kind, approverUserId)], DueAtUtc: null),
                CancellationToken.None);
        Assert.True(requested.IsSuccess, requested.Error?.Description);
        return requested.Value!.ApprovalId;
    }

    private async Task<Result> DecideAsync(
        string clientAppUserId, Guid approvalId, PortalApprovalDecision decision, string? comment = null)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<DecideMyPortalApprovalCommand>>()
            .HandleAsync(new DecideMyPortalApprovalCommand(clientAppUserId, approvalId, decision, comment));
    }

    private async Task<Result> DecideInNewScopeAsync(
        string clientAppUserId, Guid approvalId, PortalApprovalDecision decision, string? comment)
    {
        await Task.Yield();
        return await DecideAsync(clientAppUserId, approvalId, decision, comment);
    }

    private async Task<ApprovalSnapshot> ApprovalAsync(Guid approvalId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IApprovalRequester>().GetAsync(approvalId, CancellationToken.None))!;
    }

    private async Task DrainOutboxAsync()
    {
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);
    }

    private async Task<CrmDesignBrief> LoadBriefAsync(Guid briefId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CrmDbContext>()
            .CrmDesignBriefs.AsNoTracking().SingleAsync(b => b.Id == briefId);
    }

    private async Task<CrmBriefSpace> LoadEntryAsync(Guid entryId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CrmDbContext>()
            .CrmBriefSpaces.AsNoTracking().SingleAsync(e => e.Id == entryId);
    }

    private async Task<CrmRequirementItem> LoadItemAsync(Guid itemId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CrmDbContext>()
            .CrmRequirementItems.AsNoTracking().SingleAsync(i => i.Id == itemId);
    }

    private static void AssertError(Result result, int status, string code)
    {
        Assert.True(result.IsFailure, $"Expected a {status}, got success.");
        Assert.Equal(status, result.Error.Status);
        Assert.Equal(code, result.Error.Code);
    }
}
