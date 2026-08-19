using Finance.Contracts.Financial;
using Finance.Domain;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;

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
/// <para><b>Seeded at F1-07, grown at F1-07b</b> — the dashboard needed the batched per-task block one
/// task before the read slice arrived, so it landed early rather than being written inline and then
/// duplicated.</para>
///
/// <para><b>The per-TASK and per-STAGE financial blocks the backend also builds are deliberately
/// absent.</b> Their only consumers there are Projects-owned detail queries, which this port does not
/// extend; their Contracts records were removed at F1-07b for the same reason. See the note in
/// <c>Finance.Contracts.Financial</c>.</para>
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

    /// <summary>
    /// The whole-project block. <c>CollectionPercent</c> and <c>CostPercent</c> are measured against the
    /// EFFECTIVE cap rather than the original estimate, because that is the budget baseline once
    /// variations have been applied — measuring against the estimate would make an approved variation
    /// look like a cost overrun.
    /// </summary>
    public static async Task<ProjectFinancialBlock> BuildProjectBlockAsync(
        FinanceDbContext db, LedgerScopes scopes, Guid projectId, CancellationToken ct)
    {
        var budget = await db.BudgetLines
            .AsNoTracking()
            .Where(b => b.ProjectId == projectId && b.IsActive)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Allocated = g.Sum(b => b.AllocatedAmount),
                Adjustment = g.Sum(b => b.AdjustmentAllocation),
            })
            .FirstOrDefaultAsync(ct);

        var allocated = budget?.Allocated ?? 0m;
        var adjustment = budget?.Adjustment ?? 0m;
        var effective = allocated + adjustment;

        var (committed, actual) = await LedgerSums.ComputeAsync(scopes.ForProject(projectId), ct);

        var clientPaid = await db.LedgerEntries
            .AsNoTracking()
            .Where(e => e.IsActive && e.ProjectId == projectId
                        && e.Type == LedgerEntryType.ClientPayment
                        && e.Direction == LedgerDirection.In)
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;

        var vendorPaid = await db.LedgerEntries
            .AsNoTracking()
            .Where(e => e.IsActive && e.ProjectId == projectId
                        && e.Type == LedgerEntryType.ActualCost
                        && e.Direction == LedgerDirection.Out)
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;

        return new ProjectFinancialBlock(
            "EGP",
            allocated,
            adjustment,
            effective,
            allocated,
            committed,
            actual,
            effective - committed - actual,
            clientPaid,
            vendorPaid,
            effective > 0 ? clientPaid / effective * 100m : 0m,
            effective > 0 ? actual / effective * 100m : 0m,
            clientPaid - vendorPaid);
    }

    /// <summary>
    /// One row per stage that has financial activity, ordered by name.
    ///
    /// <para><b>A budget line or ledger entry is attributed to its EFFECTIVE stage</b> — its own stage
    /// if it has one, otherwise the stage of its task, otherwise the stage of its budget line. Without
    /// that fallback chain a posting made against a task-scoped budget line, naming neither the stage
    /// nor the task, would vanish from every stage row while still counting in the project total, and
    /// the two would silently disagree.</para>
    ///
    /// <para><b>DIVERGENCE: stages with no financial activity are absent</b>, where the backend lists
    /// every stage of the project with zeros. Finance cannot enumerate another module's stages —
    /// <c>IProjectDirectory</c> resolves them by id, and extending it is outside this phase's two
    /// permitted Contracts additions. Same divergence, same reason, as the dashboard's stage list.</para>
    /// </summary>
    public static async Task<List<StageBudgetRow>> BuildStageBudgetRowsAsync(
        FinanceDbContext db, IProjectDirectory projects, Guid projectId, CancellationToken ct)
    {
        var budgetLines = await db.BudgetLines
            .AsNoTracking()
            .Where(b => b.ProjectId == projectId && b.IsActive)
            .Select(b => new { b.Id, b.AllocatedAmount, b.AdjustmentAllocation, b.ProjectStageId, b.ProjectTaskId })
            .ToListAsync(ct);

        var ledgerRows = await db.LedgerEntries
            .AsNoTracking()
            .Where(e => e.ProjectId == projectId && e.IsActive
                        && (e.Type == LedgerEntryType.Commitment || e.Type == LedgerEntryType.ActualCost))
            .Select(e => new { e.ProjectStageId, e.ProjectTaskId, e.BudgetLineId, e.Type, e.Direction, e.Amount })
            .ToListAsync(ct);

        var taskIds = budgetLines.Where(b => b.ProjectTaskId != null).Select(b => b.ProjectTaskId!.Value)
            .Concat(ledgerRows.Where(e => e.ProjectTaskId != null).Select(e => e.ProjectTaskId!.Value))
            .Distinct()
            .ToArray();

        var taskStage = taskIds.Length == 0
            ? []
            : (await projects.GetTasksAsync(taskIds, ct))
                .Where(t => t.ProjectStageId != null)
                .ToDictionary(t => t.Id, t => t.ProjectStageId!.Value);

        Guid? EffectiveStage(Guid? directStage, Guid? taskId) =>
            directStage
            ?? (taskId is Guid id && taskStage.TryGetValue(id, out var viaTask) ? viaTask : null);

        var budgetLineStage = budgetLines.ToDictionary(
            b => b.Id, b => EffectiveStage(b.ProjectStageId, b.ProjectTaskId));

        var allocatedByStage = new Dictionary<Guid, decimal>();
        var adjustmentByStage = new Dictionary<Guid, decimal>();

        foreach (var line in budgetLines)
        {
            if (budgetLineStage[line.Id] is not Guid stageId)
                continue;

            allocatedByStage[stageId] = allocatedByStage.GetValueOrDefault(stageId) + line.AllocatedAmount;
            adjustmentByStage[stageId] = adjustmentByStage.GetValueOrDefault(stageId) + line.AdjustmentAllocation;
        }

        var committedSigned = new Dictionary<Guid, decimal>();
        var actualSigned = new Dictionary<Guid, decimal>();

        foreach (var row in ledgerRows)
        {
            var stageId = EffectiveStage(row.ProjectStageId, row.ProjectTaskId)
                ?? (row.BudgetLineId is Guid lineId && budgetLineStage.TryGetValue(lineId, out var viaLine)
                    ? viaLine
                    : null);

            if (stageId is not Guid sid)
                continue;

            var signed = LedgerSums.Signed(row.Direction, row.Amount);

            if (row.Type == LedgerEntryType.Commitment)
                committedSigned[sid] = committedSigned.GetValueOrDefault(sid) + signed;
            else
                actualSigned[sid] = actualSigned.GetValueOrDefault(sid) + signed;
        }

        var invoicesByStage = await db.Invoices
            .AsNoTracking()
            .Where(i => i.ProjectId == projectId && i.IsActive
                        && i.Type == InvoiceType.ClientInvoice && i.ProjectStageId != null)
            .GroupBy(i => i.ProjectStageId!.Value)
            .Select(g => new { StageId = g.Key, Sum = g.Sum(i => i.TotalAmount) })
            .ToDictionaryAsync(x => x.StageId, x => x.Sum, ct);

        var paymentsByStage = await db.LedgerEntries
            .AsNoTracking()
            .Where(e => e.ProjectId == projectId && e.IsActive
                        && e.Type == LedgerEntryType.ClientPayment && e.ProjectStageId != null)
            .GroupBy(e => e.ProjectStageId!.Value)
            .Select(g => new
            {
                StageId = g.Key,
                Sum = g.Sum(e => e.Direction == LedgerDirection.In ? e.Amount : -e.Amount),
            })
            .ToDictionaryAsync(x => x.StageId, x => x.Sum, ct);

        var stageIds = allocatedByStage.Keys
            .Concat(committedSigned.Keys)
            .Concat(actualSigned.Keys)
            .Concat(invoicesByStage.Keys)
            .Concat(paymentsByStage.Keys)
            .Distinct()
            .ToArray();

        if (stageIds.Length == 0)
            return [];

        var stages = (await projects.GetStagesAsync(stageIds, ct)).ToDictionary(s => s.Id);

        return [.. stageIds
            .Select(id =>
            {
                var actual = actualSigned.GetValueOrDefault(id);
                var committed = committedSigned.GetValueOrDefault(id) - actual;
                var allocated = allocatedByStage.GetValueOrDefault(id);
                var adjustment = adjustmentByStage.GetValueOrDefault(id);
                var effective = allocated + adjustment;

                // Order is not published by StageSummary, so every row reports 0 rather than a
                // fabricated sequence. Rows are ordered by NAME below, which is the property a caller
                // can actually rely on here.
                return new StageBudgetRow(
                    id,
                    stages.TryGetValue(id, out var stage) ? stage.Name : string.Empty,
                    0,
                    allocated,
                    adjustment,
                    effective,
                    allocated,
                    committed,
                    actual,
                    effective - committed - actual,
                    invoicesByStage.GetValueOrDefault(id),
                    paymentsByStage.GetValueOrDefault(id));
            })
            .OrderBy(r => r.Name, StringComparer.Ordinal)];
    }
}
