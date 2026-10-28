using Approvals.Contracts;
using Crm.Application.Features.Briefs;
using Crm.Contracts.Briefs;
using Crm.Contracts.Items;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;
using ApprovalDecision = Approvals.Contracts.ApprovalDecision;
using ApprovalState = Crm.Domain.ApprovalState;

namespace Crm.Application.Tests;

/// <summary>D-10 (A-F1): a row that is Pending with no bound approval id — a legacy send the backfill
/// couldn't bind because its approval was already decided — is an orphan, not "already pending". The send
/// adopts the subject's newest approval and applies its decision through the A-11 path.</summary>
public sealed partial class ItemsPeriodsTests
{
    [Fact]
    public async Task D10_ALegacyPendingItem_WhoseApprovalWasDecided_RecoversOnSend()
    {
        BriefTestProject p = await SeedAsync("d10-item");
        Guid periodId = await OpenPeriodAsync(p);
        Guid itemId = await CreateItemAsync(p, periodId);
        Assert.True((await Command(p.OwnerAppUserId, new SendCrmRequirementItemCommand(itemId, p.OwnerAppUserId))).IsSuccess);

        // The legacy state: Pending, no bound id, and the decision already made (its event found nothing to bind).
        await ExecuteSqlAsync($"""UPDATE crm.crm_requirement_items SET "PendingApprovalId" = NULL WHERE "Id" = '{itemId}'""");
        await DecideOpenApprovalAsync(p, itemId, ApprovalDecision.Rejected);
        Assert.Equal(ApprovalState.PendingApproval, (await LoadItemAsync(itemId)).ApprovalStatus);

        Result resent = await Command(p.OwnerAppUserId, new SendCrmRequirementItemCommand(itemId, p.OwnerAppUserId));

        Assert.True(resent.IsSuccess, resent.Error?.Description);
        Assert.Equal(ApprovalState.Rejected, (await LoadItemAsync(itemId)).ApprovalStatus);
    }

    [Fact]
    public async Task D10_ALegacyPendingBrief_WhoseApprovalWasDecided_RecoversOnSend()
    {
        (BriefTestProject p, Guid briefId) = await SendBriefAsync("d10-brief");
        await ExecuteSqlAsync($"""UPDATE crm.crm_design_briefs SET "PendingApprovalId" = NULL WHERE "Id" = '{briefId}'""");
        await DecideAndLoseTheEventAsync(p, CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, briefId, 1), ApprovalDecision.Rejected);

        Result resent = await Command(p.OwnerAppUserId, new SendCrmBriefCommand(briefId, p.OwnerAppUserId));

        Assert.True(resent.IsSuccess, resent.Error?.Description);
        Assert.Equal(ApprovalState.Rejected, (await LoadBriefAsync(briefId)).Status);
    }

    [Fact]
    public async Task D10_ALegacyPendingBriefSpaceEntry_WhoseApprovalWasDecided_RecoversOnSend()
    {
        BriefTestProject p = await SeedAsync("d10-entry");
        Result<Guid> brief = await CommandFor<CreateCrmDesignBriefCommand, Guid>(p.OwnerAppUserId,
            new CreateCrmDesignBriefCommand(p.ProjectId, p.EmployeeId, p.StageId,
                new Contracts.Briefs.CrmBriefHeadData((Contracts.UnitType)1, (Contracts.UnitCondition)1), p.OwnerAppUserId));
        Result<Guid> entry = await CommandFor<CreateCrmBriefSpaceCommand, Guid>(p.OwnerAppUserId,
            new CreateCrmBriefSpaceCommand(brief.Value, p.EmployeeId, p.OwnerAppUserId, SpaceId: null, Requirements: "light"));
        Assert.True((await Command(p.OwnerAppUserId, new SendCrmBriefSpaceCommand(entry.Value, p.OwnerAppUserId))).IsSuccess);
        await ExecuteSqlAsync($"""UPDATE crm.crm_brief_spaces SET "PendingApprovalId" = NULL WHERE "Id" = '{entry.Value}'""");
        await DecideAndLoseTheEventAsync(p, CrmApprovalIds.For(ApprovalSubjectType.BriefSpace, entry.Value, 1), ApprovalDecision.Rejected);

        Result resent = await Command(p.OwnerAppUserId, new SendCrmBriefSpaceCommand(entry.Value, p.OwnerAppUserId));

        Assert.True(resent.IsSuccess, resent.Error?.Description);
        await using AsyncServiceScope scope = fixture.CreateScope();
        CrmBriefSpace loaded = await scope.ServiceProvider.GetRequiredService<Crm.Infrastructure.Persistence.CrmDbContext>()
            .CrmBriefSpaces.AsNoTracking().SingleAsync(e => e.Id == entry.Value);
        Assert.Equal(ApprovalState.Rejected, loaded.ApprovalStatus);
    }

    [Fact]
    public async Task D10_ALegacyPendingItem_WithNoApprovalAtAll_IsSentAgain()
    {
        BriefTestProject p = await SeedAsync("d10-none");
        Guid periodId = await OpenPeriodAsync(p);
        Guid itemId = await CreateItemAsync(p, periodId);
        Assert.True((await Command(p.OwnerAppUserId, new SendCrmRequirementItemCommand(itemId, p.OwnerAppUserId))).IsSuccess);
        await ExecuteSqlAsync($"""UPDATE crm.crm_requirement_items SET "PendingApprovalId" = NULL WHERE "Id" = '{itemId}'""");
        await ExecuteSqlAsync($"""DELETE FROM approvals.approvals WHERE "SubjectId" = '{itemId}'""");

        Result resent = await Command(p.OwnerAppUserId, new SendCrmRequirementItemCommand(itemId, p.OwnerAppUserId));

        Assert.True(resent.IsSuccess, resent.Error?.Description);
        CrmRequirementItem item = await LoadItemAsync(itemId);
        Assert.Equal(ApprovalState.PendingApproval, item.ApprovalStatus);
        Assert.NotNull(item.PendingApprovalId);
        await DecideOpenApprovalAsync(p, itemId, ApprovalDecision.Approved);
        Assert.Equal(ApprovalState.Approved, (await LoadItemAsync(itemId)).ApprovalStatus);
    }
}
