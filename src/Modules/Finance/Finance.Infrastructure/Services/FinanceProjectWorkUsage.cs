using Finance.Contracts;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Services;

/// <summary><see cref="IFinanceProjectWorkUsage"/>: one UNION query per kind over the whole id batch.</summary>
internal sealed class FinanceProjectWorkUsage(FinanceDbContext db) : IFinanceProjectWorkUsage
{
    public async Task<IReadOnlyList<Guid>> StagesInUseAsync(IReadOnlyCollection<Guid> projectStageIds, CancellationToken ct)
    {
        if (projectStageIds.Count == 0)
        {
            return [];
        }

        return await db.BudgetLines.AsNoTracking().Where(x => x.ProjectStageId != null && projectStageIds.Contains(x.ProjectStageId.Value)).Select(x => x.ProjectStageId!.Value)
            .Union(db.LedgerEntries.AsNoTracking().Where(x => x.ProjectStageId != null && projectStageIds.Contains(x.ProjectStageId.Value)).Select(x => x.ProjectStageId!.Value))
            .Union(db.Invoices.AsNoTracking().Where(x => x.ProjectStageId != null && projectStageIds.Contains(x.ProjectStageId.Value)).Select(x => x.ProjectStageId!.Value))
            .Union(db.Variations.AsNoTracking().Where(x => x.ProjectStageId != null && projectStageIds.Contains(x.ProjectStageId.Value)).Select(x => x.ProjectStageId!.Value))
            .Union(db.BoqSnapshots.AsNoTracking().Where(x => x.ProjectStageId != null && projectStageIds.Contains(x.ProjectStageId.Value)).Select(x => x.ProjectStageId!.Value))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> TasksInUseAsync(IReadOnlyCollection<Guid> projectTaskIds, CancellationToken ct)
    {
        if (projectTaskIds.Count == 0)
        {
            return [];
        }

        return await db.BudgetLines.AsNoTracking().Where(x => x.ProjectTaskId != null && projectTaskIds.Contains(x.ProjectTaskId.Value)).Select(x => x.ProjectTaskId!.Value)
            .Union(db.LedgerEntries.AsNoTracking().Where(x => x.ProjectTaskId != null && projectTaskIds.Contains(x.ProjectTaskId.Value)).Select(x => x.ProjectTaskId!.Value))
            .Union(db.InvoiceLines.AsNoTracking().Where(x => x.ProjectTaskId != null && projectTaskIds.Contains(x.ProjectTaskId.Value)).Select(x => x.ProjectTaskId!.Value))
            .Union(db.Variations.AsNoTracking().Where(x => x.ProjectTaskId != null && projectTaskIds.Contains(x.ProjectTaskId.Value)).Select(x => x.ProjectTaskId!.Value))
            .Union(db.BoqSnapshotLines.AsNoTracking().Where(x => x.ProjectTaskId != null && projectTaskIds.Contains(x.ProjectTaskId.Value)).Select(x => x.ProjectTaskId!.Value))
            .ToListAsync(ct);
    }
}
