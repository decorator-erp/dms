using Finance.Domain;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finance.Application.Common;

/// <summary>
/// The shared reconstruction of a scope's financial position, computed at query time from Finance's own
/// aggregates. Ported from <c>../backend/Application/Common/Financial/</c>.
///
/// <para><b>The model, stated once so nothing re-derives it:</b>
/// <code>
///   EstimatedBudget     = Σ BudgetLine.AllocatedAmount        (the original estimate)
///   ApprovedAdjustments = Σ BudgetLine.AdjustmentAllocation   (applied variation deltas)
///   EffectiveCap        = EstimatedBudget + ApprovedAdjustments
///   Committed / Actual  = signed ledger sums, netted by the handoff below
///   AvailableAmount     = EffectiveCap − Committed − Actual
/// </code></para>
///
/// <para><b>Seeded at F1-07, grown at F1-07b.</b> The financial dashboard needs the batched per-task
/// block, so that one method lands here now rather than being written inline and then duplicated. The
/// stage and project blocks, and the per-stage rows, arrive with the twelve project/stage/task queries
/// that need them.</para>
/// </summary>
internal static class FinancialBlocks
{
    /// <summary>A signed ledger amount: <c>Out</c> counts positive, <c>In</c> negative. This one
    /// convention is what makes reversals cancel without any query needing to know which rows are
    /// reversals — a reversal is a mirror with the opposite direction and the same magnitude.</summary>
    public static decimal Signed(LedgerDirection direction, decimal amount) =>
        direction == LedgerDirection.Out ? amount : -amount;

    internal sealed record TaskBlock(
        decimal EstimatedBudget,
        decimal ApprovedAdjustments,
        decimal EffectiveCap,
        decimal Committed,
        decimal Actual,
        decimal Available);

    /// <summary>
    /// Per-task blocks for a set of task ids, in a bounded number of queries rather than one round trip
    /// per task.
    ///
    /// <para><b>A ledger entry is attributed to a task two ways, and both are needed.</b> Directly via
    /// <c>ProjectTaskId</c>, or indirectly via <c>BudgetLineId</c> when the entry names a budget line
    /// that is itself task-scoped. Dropping the indirect route silently under-reports every posting made
    /// against a budget line rather than against a task — which is the more common shape, since
    /// <c>InsufficientBudget</c> is checked against the budget line.</para>
    ///
    /// <para><b><c>Committed</c> is net of actuals</b>, the same commitment-to-actual handoff
    /// <c>RecalculateBudgetLineCommandHandler</c> re-derives: an actual cost both raises Actual and
    /// consumes the commitment that funded it.</para>
    /// </summary>
    public static async Task<Dictionary<Guid, TaskBlock>> BuildTaskBlocksAsync(
        FinanceDbContext db, IReadOnlyCollection<Guid> taskIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, TaskBlock>();
        if (taskIds.Count == 0)
            return result;

        var ids = taskIds.Distinct().ToArray();

        var budgetLines = await db.BudgetLines
            .AsNoTracking()
            .Where(b => b.ProjectTaskId != null && ids.Contains(b.ProjectTaskId.Value) && b.IsActive)
            .Select(b => new { b.Id, TaskId = b.ProjectTaskId!.Value, b.AllocatedAmount, b.AdjustmentAllocation })
            .ToListAsync(ct);

        var budgetLineToTask = budgetLines.ToDictionary(b => b.Id, b => b.TaskId);
        var budgetLineIds = budgetLineToTask.Keys.ToArray();

        var allocatedByTask = budgetLines
            .GroupBy(b => b.TaskId)
            .ToDictionary(g => g.Key, g => g.Sum(b => b.AllocatedAmount));

        var adjustmentByTask = budgetLines
            .GroupBy(b => b.TaskId)
            .ToDictionary(g => g.Key, g => g.Sum(b => b.AdjustmentAllocation));

        var ledgerRows = await db.LedgerEntries
            .AsNoTracking()
            .Where(e => e.IsActive
                        && (e.Type == LedgerEntryType.Commitment || e.Type == LedgerEntryType.ActualCost)
                        && ((e.ProjectTaskId != null && ids.Contains(e.ProjectTaskId.Value))
                            || (e.BudgetLineId != null && budgetLineIds.Contains(e.BudgetLineId.Value))))
            .Select(e => new { e.ProjectTaskId, e.BudgetLineId, e.Type, e.Direction, e.Amount })
            .ToListAsync(ct);

        var committedSigned = new Dictionary<Guid, decimal>();
        var actualSigned = new Dictionary<Guid, decimal>();

        foreach (var row in ledgerRows)
        {
            Guid? owner = row.ProjectTaskId
                ?? (row.BudgetLineId is Guid lineId && budgetLineToTask.TryGetValue(lineId, out var viaLine)
                    ? viaLine
                    : null);

            if (owner is not Guid taskId)
                continue;

            var signed = Signed(row.Direction, row.Amount);

            if (row.Type == LedgerEntryType.Commitment)
                committedSigned[taskId] = committedSigned.GetValueOrDefault(taskId) + signed;
            else
                actualSigned[taskId] = actualSigned.GetValueOrDefault(taskId) + signed;
        }

        foreach (var id in ids)
        {
            var actual = actualSigned.GetValueOrDefault(id);
            var committed = committedSigned.GetValueOrDefault(id) - actual;
            var allocated = allocatedByTask.GetValueOrDefault(id);
            var adjustment = adjustmentByTask.GetValueOrDefault(id);
            var effective = allocated + adjustment;

            result[id] = new TaskBlock(
                allocated, adjustment, effective, committed, actual, effective - committed - actual);
        }

        return result;
    }
}
