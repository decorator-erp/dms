using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
// Crm.Contracts and Crm.Domain each declare their own RequirementPeriodStatus (forced duplication,
// G9-01/G9-06's own remarks) - not `using Crm.Contracts;` here avoids the ambiguity; the one Contracts
// name this file needs (ICrmGateReader) is qualified inline below instead.

namespace Crm.Infrastructure.Services;

/// <summary><see cref="ICrmGateReader"/>'s implementation — the pull half of this phase's
/// push-and-pull gate seam, called by <c>Projects.Application</c>'s
/// <c>SatisfyGateConditionCommandHandler</c> for the three CRM condition types (a manual satisfy
/// verifies against real CRM state, covering the gate created after its CRM condition already became
/// true, which no satisfaction event can retroactively fire for).</summary>
internal sealed class CrmGateReader(CrmDbContext db) : Contracts.ICrmGateReader
{
    public async Task<bool> HasClosedGatheringAsync(Guid projectStageId, CancellationToken ct)
    {
        List<RequirementPeriodStatus> periods = await db.CrmRequirementPeriods.AsNoTracking()
            .Where(p => p.ProjectStageId == projectStageId)
            .Select(p => p.Status)
            .ToListAsync(ct);

        return periods.Count > 0 && periods.All(s => s != RequirementPeriodStatus.Open);
    }

    public Task<bool> IsBriefApprovedForStageAsync(Guid projectStageId, CancellationToken ct) =>
        db.CrmDesignBriefs.AsNoTracking()
            .AnyAsync(b => b.ProjectStageId == projectStageId && b.Status == ApprovalState.Approved, ct);

    public async Task<int> CountUnresolvedForStageAsync(Guid projectStageId, CancellationToken ct)
    {
        int unresolvedBriefSpaces = await UnresolvedBriefSpaces()
            .Where(e => db.CrmDesignBriefs.Any(b => b.Id == e.BriefId && b.ProjectStageId == projectStageId))
            .CountAsync(ct);

        int unresolvedItems = await UnresolvedItems()
            .Where(i => db.CrmRequirementPeriods.Any(p => p.Id == i.PeriodId && p.ProjectStageId == projectStageId))
            .CountAsync(ct);

        return unresolvedBriefSpaces + unresolvedItems;
    }

    public async Task<int> CountUnresolvedForTaskAsync(Guid projectTaskId, CancellationToken ct)
    {
        int unresolvedBriefSpaces = await UnresolvedBriefSpaces()
            .Where(e => e.ProjectTaskId == projectTaskId)
            .CountAsync(ct);

        int unresolvedItems = await UnresolvedItems()
            .Where(i => i.ProjectTaskId == projectTaskId)
            .CountAsync(ct);

        return unresolvedBriefSpaces + unresolvedItems;
    }

    /// <summary>A-4 (B-M1, owner decision): resolved means Approved or NotRequired, or superseded by a later
    /// row. Draft, PendingApproval AND Rejected are unresolved — a rejection is a request for a correction,
    /// not an answer. Gates already satisfied under the old rule stay satisfied; this applies from now on.</summary>
    private IQueryable<Domain.CrmBriefSpace> UnresolvedBriefSpaces() =>
        db.CrmBriefSpaces.AsNoTracking()
            .Where(e => e.ApprovalStatus == ApprovalState.Draft
                || e.ApprovalStatus == ApprovalState.PendingApproval
                || e.ApprovalStatus == ApprovalState.Rejected)
            .Where(e => !db.CrmBriefSpaces.Any(next => next.SupersedesSpaceId == e.Id));

    /// <summary>D-11 (A-F2, owner default): a Draft or Rejected item counts only while its period is Open —
    /// closing a period drops its unsent and rejected requests from the gate (they can no longer be sent).
    /// A PendingApproval item still counts: its decision is still coming.</summary>
    private IQueryable<Domain.CrmRequirementItem> UnresolvedItems() =>
        db.CrmRequirementItems.AsNoTracking()
            .Where(i => i.ApprovalStatus == ApprovalState.PendingApproval
                || (i.ApprovalStatus == ApprovalState.Draft || i.ApprovalStatus == ApprovalState.Rejected)
                    && db.CrmRequirementPeriods.Any(p => p.Id == i.PeriodId && p.Status == RequirementPeriodStatus.Open))
            .Where(i => !db.CrmRequirementItems.Any(next => next.SupersedesItemId == i.Id));
}
