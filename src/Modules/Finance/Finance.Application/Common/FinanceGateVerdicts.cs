using BuildingBlocks.Outbox;
using Finance.Domain;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finance.Application.Common;

/// <summary>
/// <b>The half of the Budget gate that Finance owns: deciding, not acting.</b>
///
/// <para>The backend resolves Budget gates directly from the ledger posting path —
/// <c>LedgerEntryDomainService.ResolveTaskBudgetGatesAsync</c> and <c>ResolveStageFundingGatesAsync</c>
/// read <c>gate_conditions</c>, satisfy or re-pend them, and lock or unlock the owning task or stage.
/// Every one of those is a write into another module's tables, which arch rule 3 forbids outright.</para>
///
/// <para><b>So Finance computes the verdict and publishes it; Projects acts on it.</b> This is the whole
/// reason Phase 7 ships no <c>IFinanceReader</c>: every input to both rules is a Finance column, so the
/// decision can be made here and travels as a <see cref="bool"/>. Projects never reads Finance state and
/// Finance never writes Projects state.</para>
///
/// <para><b>Both verdicts are published on EVERY relevant posting, not only on a change.</b> Finance
/// would have to remember the previous verdict to publish only transitions, and that memory would be a
/// second source of truth that can drift from the gates it describes. The consumer is idempotent —
/// satisfying an already-satisfied gate is a no-op, re-pending an already-pending one likewise — so a
/// repeated verdict costs one event and changes nothing.</para>
/// </summary>
internal static class FinanceGateVerdicts
{
    /// <summary>
    /// <b>Task budget coverage.</b> Ported from <c>ResolveTaskBudgetGatesAsync</c>: a task is covered
    /// when it is not over budget at all, or when signed client differences cover the overrun.
    ///
    /// <para>Note the comparison is <c>Actual &gt; EffectiveCap</c>, never <c>Available &lt; 0</c> —
    /// <c>ApplyActualCost</c> moves Committed and Actual in opposite directions by the same delta, so
    /// Available is invariant under an actual cost and the second form reports nothing (F1-07).</para>
    ///
    /// <para><b>Only ClientDifference variations count, and only once approved.</b> An InternalChange
    /// raises the budget line's <c>AdjustmentAllocation</c> instead, so counting it here would credit
    /// the same money twice — once in the cap and once as coverage.</para>
    /// </summary>
    public static async Task<bool> IsTaskCoveredAsync(FinanceDbContext db, Guid taskId, CancellationToken ct)
    {
        var budget = await db.BudgetLines
            .AsNoTracking()
            .Where(b => b.ProjectTaskId == taskId && b.IsActive)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Allocated = g.Sum(b => b.AllocatedAmount),
                Adjustment = g.Sum(b => b.AdjustmentAllocation),
            })
            .FirstOrDefaultAsync(ct);

        var effectiveCap = (budget?.Allocated ?? 0m) + (budget?.Adjustment ?? 0m);

        var actual = await db.LedgerEntries
            .AsNoTracking()
            .Where(e => e.ProjectTaskId == taskId && e.Type == LedgerEntryType.ActualCost && e.IsActive)
            .SumAsync(e => (decimal?)(e.Direction == LedgerDirection.Out ? e.Amount : -e.Amount), ct) ?? 0m;

        var overBudget = Math.Max(0m, actual - effectiveCap);
        if (overBudget == 0m)
            return true;

        var signedDifferences = await db.Variations
            .AsNoTracking()
            .Where(v => v.ProjectTaskId == taskId
                        && v.IsActive
                        && v.Kind == VariationKind.ClientDifference
                        && (v.Status == VariationStatus.Approved || v.Status == VariationStatus.Applied))
            .SumAsync(v => (decimal?)v.Amount, ct) ?? 0m;

        return signedDifferences >= overBudget;
    }

    /// <summary>
    /// <b>Stage funding.</b> Ported from <c>StageFundingSnapshot.IsFunded</c>:
    /// <c>ClientInvoicesSum &gt; 0 &amp;&amp; ClientPaymentsSum &gt;= ClientInvoicesSum</c>.
    ///
    /// <para><b>The <c>&gt; 0</c> half is load-bearing and is the easiest thing here to drop.</b>
    /// Without it a stage nobody has invoiced reads as funded, because zero payments trivially cover
    /// zero invoices — and a Budget gate on that stage would unlock on the first unrelated posting.</para>
    /// </summary>
    public static async Task<bool> IsStageFundedAsync(FinanceDbContext db, Guid stageId, CancellationToken ct)
    {
        var invoiced = await db.Invoices
            .AsNoTracking()
            .Where(i => i.ProjectStageId == stageId && i.Type == InvoiceType.ClientInvoice && i.IsActive)
            .SumAsync(i => (decimal?)i.TotalAmount, ct) ?? 0m;

        var paid = await db.LedgerEntries
            .AsNoTracking()
            .Where(e => e.ProjectStageId == stageId && e.Type == LedgerEntryType.ClientPayment && e.IsActive)
            .SumAsync(e => (decimal?)(e.Direction == LedgerDirection.In ? e.Amount : -e.Amount), ct) ?? 0m;

        return invoiced > 0m && paid >= invoiced;
    }

    /// <summary>
    /// Publishes whichever verdicts a posting can have changed. Called from the ledger posting and
    /// reversal paths — the F1-07 seam — and from the variation apply path, because an applied
    /// InternalChange raises a budget line's cap and can therefore restore coverage.
    /// </summary>
    public static async Task PublishAsync(
        FinanceDbContext db, Guid? projectTaskId, Guid? projectStageId, CancellationToken ct)
    {
        if (projectTaskId is Guid taskId)
        {
            var covered = await IsTaskCoveredAsync(db, taskId, ct);

            db.Outbox().Enqueue(new Finance.Contracts.TaskBudgetCoverageChangedIntegrationEvent(
                Guid.NewGuid(), taskId, covered, DateTimeOffset.UtcNow));
        }

        if (projectStageId is Guid stageId)
        {
            var funded = await IsStageFundedAsync(db, stageId, ct);

            db.Outbox().Enqueue(new Finance.Contracts.StageFundingChangedIntegrationEvent(
                Guid.NewGuid(), stageId, funded, DateTimeOffset.UtcNow));
        }
    }
}
