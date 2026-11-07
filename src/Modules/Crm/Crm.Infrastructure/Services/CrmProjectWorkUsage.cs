using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crm.Infrastructure.Services;

/// <summary><see cref="Contracts.ICrmProjectWorkUsage"/>: one query per kind, set-based over the whole id
/// batch.</summary>
internal sealed class CrmProjectWorkUsage(CrmDbContext db) : Contracts.ICrmProjectWorkUsage
{
    public async Task<IReadOnlyList<Guid>> StagesInUseAsync(IReadOnlyCollection<Guid> projectStageIds, CancellationToken ct)
    {
        if (projectStageIds.Count == 0)
        {
            return [];
        }

        return await db.CrmDesignBriefs.AsNoTracking()
            .Where(b => projectStageIds.Contains(b.ProjectStageId))
            .Select(b => b.ProjectStageId)
            .Union(db.CrmRequirementPeriods.AsNoTracking()
                .Where(p => p.ProjectStageId != null && projectStageIds.Contains(p.ProjectStageId.Value))
                .Select(p => p.ProjectStageId!.Value))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> TasksInUseAsync(IReadOnlyCollection<Guid> projectTaskIds, CancellationToken ct)
    {
        if (projectTaskIds.Count == 0)
        {
            return [];
        }

        return await db.CrmRequirementItems.AsNoTracking()
            .Where(i => i.ProjectTaskId != null && projectTaskIds.Contains(i.ProjectTaskId.Value))
            .Select(i => i.ProjectTaskId!.Value)
            .Union(db.CrmBriefSpaces.AsNoTracking()
                .Where(s => s.ProjectTaskId != null && projectTaskIds.Contains(s.ProjectTaskId.Value))
                .Select(s => s.ProjectTaskId!.Value))
            .ToListAsync(ct);
    }
}
