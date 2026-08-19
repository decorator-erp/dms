using Finance.Domain;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;

namespace Finance.Application.Common;

/// <summary>
/// Builds the <see cref="LedgerEntry"/> query scoped to a project, stage or task. Ported from
/// <c>../backend/Application/Common/Financial/LedgerScopes.cs</c>.
///
/// <para><b>A scope is a union of three routes, not one column.</b> An entry belongs to a task if it
/// names the task directly OR names a budget line the task owns; to a stage if it names the stage, a
/// task of that stage, or a budget line under either. Collapsing that to the direct column alone
/// silently under-reports every posting made against a budget line — which is the more common shape,
/// since <c>InsufficientBudget</c> is checked against the line.</para>
///
/// <para><b>The stage scope is the one that needs Projects, and it is exact rather than approximate.</b>
/// The backend resolves "tasks of this stage" from its own <c>ProjectTasks</c> table; Finance cannot.
/// Instead the candidate task ids are the ones appearing on this project's own ledger entries and
/// budget lines, resolved in one batched <c>GetTasksAsync</c> and filtered by
/// <c>TaskSummary.ProjectStageId</c>. That is not a narrowing: a task Finance has no financial data for
/// contributes nothing to any sum, so the two routes reach the same numbers.</para>
/// </summary>
internal sealed class LedgerScopes(FinanceDbContext db, IProjectDirectory projects)
{
    public IQueryable<LedgerEntry> ForProject(Guid projectId) =>
        db.LedgerEntries.AsNoTracking().Where(e => e.IsActive && e.ProjectId == projectId);

    /// <summary>Entries posted directly to the task, or to a budget line the task owns.</summary>
    public IQueryable<LedgerEntry> ForTask(Guid taskId)
    {
        var budgetLineIds = db.BudgetLines
            .Where(b => b.ProjectTaskId == taskId && b.IsActive)
            .Select(b => b.Id);

        return db.LedgerEntries.AsNoTracking().Where(e => e.IsActive && (
            e.ProjectTaskId == taskId
            || (e.BudgetLineId != null && budgetLineIds.Contains(e.BudgetLineId.Value))));
    }

    /// <summary>
    /// Entries posted directly to the stage, to one of its tasks, or to a budget line owned by the stage
    /// or by one of its tasks. Returns <c>null</c> when the stage does not exist, so callers can
    /// distinguish "no such stage" from "a stage with no movements".
    /// </summary>
    public async Task<IQueryable<LedgerEntry>?> ForStageAsync(Guid stageId, CancellationToken ct)
    {
        var stage = await projects.GetStageAsync(stageId, ct);
        if (stage is null)
            return null;

        var stageTaskIds = await StageTaskIdsAsync(stage.ProjectId, stageId, ct);

        // A task-scoped budget line always carries its task's stage -- CreateBudgetLineCommandHandler
        // refuses one that does not -- so the direct column covers both shapes here.
        var budgetLineIds = db.BudgetLines
            .Where(b => b.IsActive
                        && (b.ProjectStageId == stageId
                            || (b.ProjectTaskId != null && stageTaskIds.Contains(b.ProjectTaskId.Value))))
            .Select(b => b.Id);

        return db.LedgerEntries.AsNoTracking().Where(e => e.IsActive && (
            e.ProjectStageId == stageId
            || (e.ProjectTaskId != null && stageTaskIds.Contains(e.ProjectTaskId.Value))
            || (e.BudgetLineId != null && budgetLineIds.Contains(e.BudgetLineId.Value))));
    }

    /// <summary>The tasks of <paramref name="stageId"/> that Finance has any data for — see the
    /// type-level note for why that is the same set for every purpose here.</summary>
    public async Task<Guid[]> StageTaskIdsAsync(Guid projectId, Guid stageId, CancellationToken ct)
    {
        var fromLedger = await db.LedgerEntries
            .Where(e => e.ProjectId == projectId && e.IsActive && e.ProjectTaskId != null)
            .Select(e => e.ProjectTaskId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var fromBudgetLines = await db.BudgetLines
            .Where(b => b.ProjectId == projectId && b.IsActive && b.ProjectTaskId != null)
            .Select(b => b.ProjectTaskId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var candidates = fromLedger.Concat(fromBudgetLines).Distinct().ToArray();
        if (candidates.Length == 0)
            return [];

        var tasks = await projects.GetTasksAsync(candidates, ct);

        return [.. tasks.Where(t => t.ProjectStageId == stageId).Select(t => t.Id)];
    }
}
