using Approvals.Contracts;
using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Crm.Contracts.Briefs;
using Crm.Contracts.Portal;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>B1.3 / B1.5: the client's approvals inbox — only the caller's rows, pending vs decided, enriched in
/// batch, paging bounds 400.</summary>
public sealed partial class PortalApprovalTests
{
    [Fact]
    public async Task List_ShowsOnlyTheCallersRows_PendingThenDecided_Enriched()
    {
        BriefTestProject p = await SeedAsync("portal-list");
        BriefTestProject other = await SeedAsync("portal-list-other");
        Guid briefId = await CreateBriefAsync(p);
        Assert.True((await AsOwner(p, new SendCrmBriefCommand(briefId, p.OwnerAppUserId))).IsSuccess);
        Guid briefApproval = (await LoadBriefAsync(briefId)).PendingApprovalId!.Value;
        Guid entryId = await CreateEntryAsync(p, briefId);
        Assert.True((await AsOwner(p, new SendCrmBriefSpaceCommand(entryId, p.OwnerAppUserId))).IsSuccess);
        Guid entryApproval = (await LoadEntryAsync(entryId)).PendingApprovalId!.Value;
        Guid itemId = await CreateSentItemAsync(p);
        Guid itemApproval = (await LoadItemAsync(itemId)).PendingApprovalId!.Value;
        Assert.True((await DecideAsync(p.ClientAppUserId, itemApproval, PortalApprovalDecision.ChangesRequested, "Other tile")).IsSuccess);
        await DrainOutboxAsync();
        (_, Guid othersApproval) = await SentBriefAsync("portal-list-third");
        Guid otherBrief = await CreateBriefAsync(other);
        Assert.True((await AsOwner(other, new SendCrmBriefCommand(otherBrief, other.OwnerAppUserId))).IsSuccess);

        PagedResult<PortalApprovalResponse> pending = (await ListAsync(p.ClientAppUserId, "pending")).Value!;
        PagedResult<PortalApprovalResponse> decided = (await ListAsync(p.ClientAppUserId, "DECIDED")).Value!;

        Assert.Equal([entryApproval, briefApproval], pending.Items.Select(i => i.Id));
        Assert.Equal(2, pending.TotalCount);
        PortalApprovalResponse brief = pending.Items.Single(i => i.Id == briefApproval);
        Assert.Equal(
            (PortalApprovalSubjectType.DesignBrief, briefId, p.ProjectId, "Brief Project", (Guid?)briefId, (Guid?)p.StageId, "Stage 1", (int?)1, (string?)null),
            (brief.SubjectType, brief.SubjectId, brief.ProjectId, brief.ProjectName, brief.BriefId, brief.StageId, brief.StageName, brief.Round, brief.Title));
        Assert.Null(brief.Decision);
        PortalApprovalResponse entry = pending.Items.Single(i => i.Id == entryApproval);
        Assert.Equal((PortalApprovalSubjectType.BriefSpace, (Guid?)briefId, (int?)1), (entry.SubjectType, entry.BriefId, entry.Round));

        PortalApprovalResponse item = Assert.Single(decided.Items);
        Assert.Equal(
            (itemApproval, PortalApprovalSubjectType.RequirementItem, "Matte flooring", (int?)1, (PortalApprovalDecision?)PortalApprovalDecision.ChangesRequested, "Other tile"),
            (item.Id, item.SubjectType, item.Title, item.Round, item.Decision, item.Comment));
        Assert.NotNull(item.DecidedAtUtc);
        Assert.DoesNotContain(pending.Items.Concat(decided.Items), i => i.Id == othersApproval);
    }

    [Fact]
    public async Task List_DefaultsToPending_AndPagesOnTheApprovalsSide()
    {
        BriefTestProject p = await SeedAsync("portal-page");
        Guid briefId = await CreateBriefAsync(p);
        Assert.True((await AsOwner(p, new SendCrmBriefCommand(briefId, p.OwnerAppUserId))).IsSuccess);
        Guid entryId = await CreateEntryAsync(p, briefId);
        Assert.True((await AsOwner(p, new SendCrmBriefSpaceCommand(entryId, p.OwnerAppUserId))).IsSuccess);

        PagedResult<PortalApprovalResponse> first = (await ListAsync(p.ClientAppUserId, null, 1, 1)).Value!;
        PagedResult<PortalApprovalResponse> second = (await ListAsync(p.ClientAppUserId, null, 2, 1)).Value!;

        Assert.Equal((1, 2), (first.Items.Count, first.TotalCount));
        Assert.Single(second.Items);
        Assert.NotEqual(first.Items[0].Id, second.Items[0].Id);
    }

    /// <summary>An approval that names the client but whose subject is on another client's project is dropped
    /// (and logged); a gate-condition approval naming the client never reaches the page at all.</summary>
    [Fact]
    public async Task List_DropsARowOnAnotherClientsProject_AndIgnoresNonCrmSubjects()
    {
        BriefTestProject p = await SeedAsync("portal-drop");
        BriefTestProject other = await SeedAsync("portal-drop-other");
        Guid othersBrief = await CreateBriefAsync(other);
        Guid foreign = await RequestDirectlyAsync(p, ApprovalSubjectType.DesignBrief, othersBrief, ApproverKind.Client, p.ClientAppUserId);
        await RequestDirectlyAsync(p, ApprovalSubjectType.GateCondition, Guid.NewGuid(), ApproverKind.Client, p.ClientAppUserId);

        PagedResult<PortalApprovalResponse> pending = (await ListAsync(p.ClientAppUserId, "pending")).Value!;

        Assert.DoesNotContain(pending.Items, i => i.Id == foreign);
        Assert.Empty(pending.Items);
        Assert.Equal(1, pending.TotalCount); // the dropped row is counted (documented over-count); the gate one is not
    }

    [Theory]
    [InlineData("pending", 0, 50)]
    [InlineData("pending", 1_000_001, 50)]
    [InlineData("pending", 1, 0)]
    [InlineData("pending", 1, 201)]
    [InlineData("open", 1, 50)]
    public async Task List_OutOfBoundsPagingOrAnUnknownStatus_Is400(string status, int pageNumber, int pageSize)
    {
        BriefTestProject p = await SeedAsync("portal-bounds");

        Result<PagedResult<PortalApprovalResponse>> result = await ListAsync(p.ClientAppUserId, status, pageNumber, pageSize);

        Assert.True(result.IsFailure);
        Assert.Equal(400, result.Error.Status);
    }

    [Fact]
    public async Task List_AnUnknownStatus_IsCrmPortalInvalidStatus()
    {
        BriefTestProject p = await SeedAsync("portal-status");

        Assert.Equal("crm.portal.InvalidStatus", (await ListAsync(p.ClientAppUserId, "all")).Error.Code);
    }

    private async Task<Result<PagedResult<PortalApprovalResponse>>> ListAsync(
        string clientAppUserId, string? status, int pageNumber = 1, int pageSize = 50)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetMyPortalApprovalsQuery, PagedResult<PortalApprovalResponse>>>()
            .HandleAsync(new GetMyPortalApprovalsQuery(clientAppUserId, status, pageNumber, pageSize));
    }
}
