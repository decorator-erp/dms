using Approvals.Contracts;
using Crm.Application.Features.Briefs;
using Crm.Contracts.Briefs;
using Crm.Contracts.Items;
using Crm.Contracts.Periods;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;
using ApprovalDecision = Approvals.Contracts.ApprovalDecision;
using ApprovalState = Crm.Domain.ApprovalState;

namespace Crm.Application.Tests;

/// <summary>L-1 and L-2 (CRM production review), on D-10's recovery of a legacy pending row with no bound
/// approval id. L-1: the subject's newest approval is adopted only when it is undecided or is the row's own
/// last send; an older round's decision (already applied before the row was sent again) is never re-applied,
/// and the row is re-requested under a fresh round id instead. L-2: that re-request is a new send, so an
/// item's closed period refuses it.</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public async Task L1_ALegacyPendingItem_WhoseNewestApprovalIsAnOlderRoundsRejection_IsReRequested_NotRejected()
    {
        BriefTestProject p = await SeedAsync("l1-item");
        Guid periodId = await OpenPeriodAsync(p);
        Guid itemId = await CreateItemAsync(p, periodId);
        Assert.True((await Command(p.OwnerAppUserId, new SendCrmRequirementItemCommand(itemId, p.OwnerAppUserId))).IsSuccess);
        await DecideOpenApprovalAsync(p, itemId, ApprovalDecision.Rejected);
        Assert.Equal(ApprovalState.Rejected, (await LoadItemAsync(itemId)).ApprovalStatus);

        // Round 2 is sent, then loses both its binding and its approval: the newest approval left is round 1's.
        Assert.True((await Command(p.OwnerAppUserId, new SendCrmRequirementItemCommand(itemId, p.OwnerAppUserId))).IsSuccess);
        Guid round2 = CrmApprovalIds.For(ApprovalSubjectType.RequirementItem, itemId, 2);
        await ExecuteSqlAsync($"""UPDATE crm.crm_requirement_items SET "PendingApprovalId" = NULL WHERE "Id" = '{itemId}'""");
        await ExecuteSqlAsync($"""DELETE FROM approvals.approvals WHERE "Id" = '{round2}'""");

        Result resent = await Command(p.OwnerAppUserId, new SendCrmRequirementItemCommand(itemId, p.OwnerAppUserId));

        Assert.True(resent.IsSuccess, resent.Error?.Description);
        CrmRequirementItem item = await LoadItemAsync(itemId);
        Assert.Equal(ApprovalState.PendingApproval, item.ApprovalStatus);
        Assert.Equal(CrmApprovalIds.For(ApprovalSubjectType.RequirementItem, itemId, 3), item.PendingApprovalId);
        await DecideOpenApprovalAsync(p, itemId, ApprovalDecision.Approved);
        Assert.Equal(ApprovalState.Approved, (await LoadItemAsync(itemId)).ApprovalStatus);
    }

    [Fact]
    public async Task L1_ALegacyPendingBriefSpaceEntry_WhoseNewestApprovalIsAnOlderRoundsRejection_IsReRequested()
    {
        BriefTestProject p = await SeedAsync("l1-entry");
        Result<Guid> brief = await CommandFor<CreateCrmDesignBriefCommand, Guid>(p.OwnerAppUserId,
            new CreateCrmDesignBriefCommand(p.ProjectId, p.EmployeeId, p.StageId,
                new Contracts.Briefs.CrmBriefHeadData((Contracts.UnitType)1, (Contracts.UnitCondition)1), p.OwnerAppUserId));
        Result<Guid> entry = await CommandFor<CreateCrmBriefSpaceCommand, Guid>(p.OwnerAppUserId,
            new CreateCrmBriefSpaceCommand(brief.Value, p.EmployeeId, p.OwnerAppUserId, SpaceId: null, Requirements: "light"));
        Guid entryId = entry.Value;
        Assert.True((await Command(p.OwnerAppUserId, new SendCrmBriefSpaceCommand(entryId, p.OwnerAppUserId))).IsSuccess);
        await DecideOpenApprovalAsync(p, entryId, ApprovalDecision.Rejected);
        Assert.Equal(ApprovalState.Rejected, (await LoadEntryAsync(entryId)).ApprovalStatus);

        Assert.True((await Command(p.OwnerAppUserId, new SendCrmBriefSpaceCommand(entryId, p.OwnerAppUserId))).IsSuccess);
        Guid round2 = CrmApprovalIds.For(ApprovalSubjectType.BriefSpace, entryId, 2);
        await ExecuteSqlAsync($"""UPDATE crm.crm_brief_spaces SET "PendingApprovalId" = NULL WHERE "Id" = '{entryId}'""");
        await ExecuteSqlAsync($"""DELETE FROM approvals.approvals WHERE "Id" = '{round2}'""");

        Result resent = await Command(p.OwnerAppUserId, new SendCrmBriefSpaceCommand(entryId, p.OwnerAppUserId));

        Assert.True(resent.IsSuccess, resent.Error?.Description);
        CrmBriefSpace loaded = await LoadEntryAsync(entryId);
        Assert.Equal(ApprovalState.PendingApproval, loaded.ApprovalStatus);
        Assert.Equal(CrmApprovalIds.For(ApprovalSubjectType.BriefSpace, entryId, 3), loaded.PendingApprovalId);
    }

    [Fact]
    public async Task L1_ALegacyPendingBrief_WhoseNewestApprovalIsAnOlderRoundsRejection_OpensAFreshRound()
    {
        (BriefTestProject p, Guid briefId) = await SendBriefAsync("l1-brief");
        await DecideOpenApprovalAsync(p, briefId, ApprovalDecision.Rejected);
        Assert.Equal(ApprovalState.Rejected, (await LoadBriefAsync(briefId)).Status);

        Assert.True((await Command(p.OwnerAppUserId, new SendCrmBriefCommand(briefId, p.OwnerAppUserId))).IsSuccess);
        Guid round2 = CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, briefId, 2);
        await ExecuteSqlAsync($"""UPDATE crm.crm_design_briefs SET "PendingApprovalId" = NULL WHERE "Id" = '{briefId}'""");
        await ExecuteSqlAsync($"""DELETE FROM approvals.approvals WHERE "Id" = '{round2}'""");

        Result resent = await Command(p.OwnerAppUserId, new SendCrmBriefCommand(briefId, p.OwnerAppUserId));

        Assert.True(resent.IsSuccess, resent.Error?.Description);
        CrmDesignBrief loaded = await LoadBriefAsync(briefId);
        Assert.Equal(ApprovalState.PendingApproval, loaded.Status);
        Assert.Equal(CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, briefId, 3), loaded.PendingApprovalId);
    }

    [Fact]
    public async Task L2_ALegacyPendingItem_ReRequestedInAClosedPeriod_Is409()
    {
        BriefTestProject p = await SeedAsync("l2-closed");
        Guid periodId = await OpenPeriodAsync(p);
        Guid itemId = await CreateItemAsync(p, periodId);
        Assert.True((await Command(p.OwnerAppUserId, new SendCrmRequirementItemCommand(itemId, p.OwnerAppUserId))).IsSuccess);
        await ExecuteSqlAsync($"""UPDATE crm.crm_requirement_items SET "PendingApprovalId" = NULL WHERE "Id" = '{itemId}'""");
        await ExecuteSqlAsync($"""DELETE FROM approvals.approvals WHERE "SubjectId" = '{itemId}'""");
        Assert.True((await Command(p.OwnerAppUserId, new CloseCrmRequirementPeriodCommand(periodId, p.OwnerAppUserId))).IsSuccess);

        AssertError(await Command(p.OwnerAppUserId, new SendCrmRequirementItemCommand(itemId, p.OwnerAppUserId)), 409, "crm.periods.NotOpen");

        CrmRequirementItem item = await LoadItemAsync(itemId);
        Assert.Null(item.PendingApprovalId);
        Assert.Equal(1, item.SendCount);
    }

    private async Task<CrmBriefSpace> LoadEntryAsync(Guid entryId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<Crm.Infrastructure.Persistence.CrmDbContext>()
            .CrmBriefSpaces.AsNoTracking().SingleAsync(e => e.Id == entryId);
    }
}
