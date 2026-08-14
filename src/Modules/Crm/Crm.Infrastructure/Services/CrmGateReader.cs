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
        var periods = await db.CrmRequirementPeriods.AsNoTracking()
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
        var unresolvedBriefSpaces = await db.CrmBriefSpaces.AsNoTracking()
            .Where(e => db.CrmDesignBriefs.Any(b => b.Id == e.BriefId && b.ProjectStageId == projectStageId))
            .Where(e => e.ApprovalStatus == ApprovalState.Draft || e.ApprovalStatus == ApprovalState.PendingApproval)
            .CountAsync(ct);

        var unresolvedItems = await db.CrmRequirementItems.AsNoTracking()
            .Where(i => db.CrmRequirementPeriods.Any(p => p.Id == i.PeriodId && p.ProjectStageId == projectStageId))
            .Where(i => i.ApprovalStatus == ApprovalState.Draft || i.ApprovalStatus == ApprovalState.PendingApproval)
            .CountAsync(ct);

        return unresolvedBriefSpaces + unresolvedItems;
    }

    public async Task<int> CountUnresolvedForTaskAsync(Guid projectTaskId, CancellationToken ct)
    {
        var unresolvedBriefSpaces = await db.CrmBriefSpaces.AsNoTracking()
            .Where(e => e.ProjectTaskId == projectTaskId)
            .Where(e => e.ApprovalStatus == ApprovalState.Draft || e.ApprovalStatus == ApprovalState.PendingApproval)
            .CountAsync(ct);

        var unresolvedItems = await db.CrmRequirementItems.AsNoTracking()
            .Where(i => i.ProjectTaskId == projectTaskId)
            .Where(i => i.ApprovalStatus == ApprovalState.Draft || i.ApprovalStatus == ApprovalState.PendingApproval)
            .CountAsync(ct);

        return unresolvedBriefSpaces + unresolvedItems;
    }
}
