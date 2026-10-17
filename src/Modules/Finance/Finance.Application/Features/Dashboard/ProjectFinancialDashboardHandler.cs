using BuildingBlocks.Messaging;
using Finance.Application.Common;
using Finance.Contracts.Dashboard;
using Finance.Domain;
using Finance.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Finance.Application.Features.Dashboard;

internal sealed class GetProjectFinancialDashboardQueryValidator
    : AbstractValidator<GetProjectFinancialDashboardQuery>
{
    public GetProjectFinancialDashboardQueryValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.RecentActivityCount).InclusiveBetween(1, 100);
    }
}

/// <summary>
/// Ported from <c>../backend/Application/Features/Finance/Queries/Dashboard/</c>. One project's whole
/// financial position, assembled from Finance's own tables plus two batched name lookups.
///
/// <para><b>Every figure is computed here, none is stored.</b> The Projects entities carry no financial
/// state at all since the consultant/execution split — <c>RequiredBudget</c>, <c>ClientPaidAmount</c>,
/// <c>ActualCost</c> and <c>BudgetStatus</c> columns are gone. Stage funding is client invoices versus
/// client payments; over-budget is actual versus effective cap. Reintroducing any of those columns
/// would create a second source of truth that drifts.</para>
///
/// <para><b>DIVERGENCE: the stage list is derived from financial activity, not from the project's full
/// stage list.</b> The backend enumerates every stage of the project and shows zero rows for stages with
/// nothing financial against them. Finance cannot enumerate another module's stages —
/// <c>IProjectDirectory</c> resolves stages BY ID, and extending it is outside this phase's two
/// permitted Contracts additions. So the stages reported are those appearing in Finance's own budget
/// lines, invoices or ledger entries, with names fetched by id. <b>What changes:</b> a stage with no
/// financial activity is absent rather than present with zeros. What does not change: any stage with an
/// invoice, a payment or a budget line is present and its figures are identical. Recorded because it is
/// a visible difference in a response, not an internal one.</para>
///
/// <para><b>The over-budget task list is NOT affected by the same limitation</b>, and it is worth
/// saying why rather than leaving it to be re-derived. The backend lists every task and then keeps only
/// those where actual exceeds the effective cap. A task with no budget line and no ledger entry has
/// both at zero, so it can never pass that filter — the set reached from Finance's own data is exactly
/// the set the backend reaches by filtering.</para>
///
/// <para><b><see cref="ProjectFinancialDashboardResponse.Currency"/> is the constant "EGP"</b>, as in
/// the backend. Multi-currency is a recorded out-of-scope decision, not an oversight.</para>
/// </summary>
internal sealed class GetProjectFinancialDashboardQueryHandler(FinanceDbContext db, IProjectDirectory projects)
    : IQueryHandler<GetProjectFinancialDashboardQuery, ProjectFinancialDashboardResponse>
{
    private const string Currency = "EGP";

    public async Task<Result<ProjectFinancialDashboardResponse>> HandleAsync(
        GetProjectFinancialDashboardQuery query, CancellationToken ct = default)
    {
        Guid projectId = query.ProjectId;

        if (await projects.GetProjectAsync(projectId, ct) is null)
        {
            return FinanceDashboardErrors.ProjectNotFound;
        }

        BudgetSummary budget = await BudgetSummaryAsync(projectId, ct);
        CollectionSummary collection = await CollectionSummaryAsync(projectId, budget.EffectiveCap, ct);
        EarningsSummary earnings = await EarningsSummaryAsync(projectId, budget.EffectiveCap, ct);
        List<BudgetLineRow> budgetLines = await BudgetLineRowsAsync(projectId, ct);
        InvoiceSummary invoices = await InvoiceSummaryAsync(projectId, ct);
        VariationSummary variations = await VariationSummaryAsync(projectId, ct);
        List<OverBudgetTaskRow> overBudgetTasks = await OverBudgetTasksAsync(projectId, ct);
        BoqSummary? boq = await BoqSummaryAsync(projectId, ct);
        List<RecentLedgerRow> recent = await RecentActivityAsync(projectId, query.RecentActivityCount, ct);

        return new ProjectFinancialDashboardResponse(
            projectId, Currency, budget, collection, earnings, budgetLines, invoices, variations,
            overBudgetTasks, boq, recent);
    }

    private async Task<BudgetSummary> BudgetSummaryAsync(Guid projectId, CancellationToken ct)
    {
        var totals = await db.BudgetLines
            .Where(b => b.ProjectId == projectId && b.IsActive)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Allocated = g.Sum(b => b.AllocatedAmount),
                Adjustment = g.Sum(b => b.AdjustmentAllocation),
                Committed = g.Sum(b => b.CommittedAmount),
                Actual = g.Sum(b => b.ActualAmount),
            })
            .SingleOrDefaultAsync(ct);

        decimal allocated = totals?.Allocated ?? 0m;
        decimal adjustment = totals?.Adjustment ?? 0m;
        decimal committed = totals?.Committed ?? 0m;
        decimal actual = totals?.Actual ?? 0m;
        decimal effective = allocated + adjustment;

        // ActualBudget is null in the backend too -- the column it once projected is gone, and the field
        // is kept on the wire rather than removed from a published contract.
        return new BudgetSummary(
            allocated, adjustment, effective, null,
            allocated, committed, actual, effective - committed - actual);
    }

    private async Task<CollectionSummary> CollectionSummaryAsync(
        Guid projectId, decimal effectiveCap, CancellationToken ct)
    {
        decimal clientPaymentsTotal = await db.LedgerEntries
            .Where(e => e.ProjectId == projectId
                        && e.IsActive
                        && e.Type == LedgerEntryType.ClientPayment
                        && e.Direction == LedgerDirection.In)
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;

        Dictionary<Guid, decimal> invoicesByStage = await db.Invoices
            .Where(i => i.ProjectId == projectId && i.IsActive
                        && i.Type == InvoiceType.ClientInvoice && i.ProjectStageId != null)
            .GroupBy(i => i.ProjectStageId!.Value)
            .Select(g => new { StageId = g.Key, Sum = g.Sum(i => i.TotalAmount) })
            .ToDictionaryAsync(x => x.StageId, x => x.Sum, ct);

        Dictionary<Guid, decimal> paymentsByStage = await db.LedgerEntries
            .Where(e => e.ProjectId == projectId && e.IsActive
                        && e.Type == LedgerEntryType.ClientPayment && e.ProjectStageId != null)
            .GroupBy(e => e.ProjectStageId!.Value)
            .Select(g => new
            {
                StageId = g.Key,
                Sum = g.Sum(e => e.Direction == LedgerDirection.In ? e.Amount : -e.Amount),
            })
            .ToDictionaryAsync(x => x.StageId, x => x.Sum, ct);

        List<Guid> budgetStages = await db.BudgetLines
            .Where(b => b.ProjectId == projectId && b.IsActive && b.ProjectStageId != null)
            .Select(b => b.ProjectStageId!.Value)
            .Distinct()
            .ToListAsync(ct);

        Guid[] stageIds = invoicesByStage.Keys
            .Concat(paymentsByStage.Keys)
            .Concat(budgetStages)
            .Distinct()
            .ToArray();

        Dictionary<Guid, string> names = stageIds.Length == 0
            ? []
            : (await projects.GetStagesAsync(stageIds, ct)).ToDictionary(s => s.Id, s => s.Name);

        var stages = stageIds
            .Select(id =>
            {
                decimal invoiced = invoicesByStage.GetValueOrDefault(id);
                decimal paid = paymentsByStage.GetValueOrDefault(id);

                // IsFunded, stated once here and identically in the Budget gate at F1-08. The
                // `invoiced > 0` half is load-bearing: without it a stage that has never been invoiced
                // reads as funded, because zero payments trivially cover zero invoices.
                return new StageFundingRow(
                    id, names.GetValueOrDefault(id) ?? string.Empty, invoiced, paid,
                    invoiced > 0 && paid >= invoiced);
            })
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .ToList();

        decimal percent = effectiveCap > 0 ? clientPaymentsTotal / effectiveCap * 100m : 0m;

        return new CollectionSummary(clientPaymentsTotal, percent, stages);
    }

    private async Task<EarningsSummary> EarningsSummaryAsync(
        Guid projectId, decimal effectiveCap, CancellationToken ct)
    {
        var totals = await db.LedgerEntries
            .Where(e => e.ProjectId == projectId && e.IsActive)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                In = g.Where(e => e.Direction == LedgerDirection.In).Sum(e => (decimal?)e.Amount) ?? 0m,
                Out = g.Where(e => e.Direction == LedgerDirection.Out).Sum(e => (decimal?)e.Amount) ?? 0m,
            })
            .SingleOrDefaultAsync(ct);

        decimal totalIn = totals?.In ?? 0m;
        decimal totalOut = totals?.Out ?? 0m;
        decimal net = totalIn - totalOut;

        return new EarningsSummary(
            totalIn, totalOut, net, effectiveCap > 0 ? net / effectiveCap * 100m : 0m);
    }

    /// <summary>The category name comes from a correlated subquery rather than a join, because the
    /// relationship is optional — <c>CostCategoryId</c> is nullable and the category may since have been
    /// deactivated — and an inner join would silently drop those rows from a financial summary.</summary>
    private Task<List<BudgetLineRow>> BudgetLineRowsAsync(Guid projectId, CancellationToken ct) =>
        db.BudgetLines
            .Where(b => b.ProjectId == projectId && b.IsActive)
            .OrderBy(b => db.CostCategories
                .Where(c => c.Id == b.CostCategoryId).Select(c => c.Name).FirstOrDefault())
            .ThenBy(b => b.Name)
            .Select(b => new BudgetLineRow(
                b.Id,
                b.CostCategoryId,
                db.CostCategories.Where(c => c.Id == b.CostCategoryId).Select(c => c.Name).FirstOrDefault(),
                b.Name,
                b.AllocatedAmount,
                b.AdjustmentAllocation,
                b.CommittedAmount,
                b.ActualAmount,
                b.AllocatedAmount + b.AdjustmentAllocation - b.CommittedAmount - b.ActualAmount))
            .ToListAsync(ct);

    private async Task<InvoiceSummary> InvoiceSummaryAsync(Guid projectId, CancellationToken ct)
    {
        var buckets = await db.Invoices
            .Where(i => i.ProjectId == projectId && i.IsActive)
            .GroupBy(i => i.Status)
            .Select(g => new { g.Key, Count = g.Count(), Total = g.Sum(i => i.TotalAmount) })
            .ToListAsync(ct);

        decimal outstanding = await db.Invoices
            .Where(i => i.ProjectId == projectId
                        && i.IsActive
                        && i.Status == InvoiceStatus.Open
                        && i.TotalAmount - i.PaidAmount > 0)
            .SumAsync(i => (decimal?)(i.TotalAmount - i.PaidAmount), ct) ?? 0m;

        return new InvoiceSummary(
            buckets.Sum(b => b.Count),
            outstanding,
            [.. buckets.Select(b => new InvoiceStatusBucket(b.Key.ToString(), b.Count, b.Total))]);
    }

    private async Task<VariationSummary> VariationSummaryAsync(Guid projectId, CancellationToken ct)
    {
        var buckets = await db.Variations
            .Where(v => v.ProjectId == projectId && v.IsActive)
            .GroupBy(v => new { v.Kind, v.Status })
            .Select(g => new { g.Key.Kind, g.Key.Status, Count = g.Count(), Total = g.Sum(v => v.Amount) })
            .ToListAsync(ct);

        decimal signedTotal = await db.Variations
            .Where(v => v.ProjectId == projectId
                        && v.IsActive
                        && v.Kind == VariationKind.ClientDifference
                        && (v.Status == VariationStatus.Approved || v.Status == VariationStatus.Applied))
            .SumAsync(v => (decimal?)v.Amount, ct) ?? 0m;

        return new VariationSummary(
            buckets.Sum(b => b.Count),
            signedTotal,
            [.. buckets.Select(b => new VariationBucket(
                b.Kind.ToString(), b.Status.ToString(), b.Count, b.Total))]);
    }

    private async Task<List<OverBudgetTaskRow>> OverBudgetTasksAsync(Guid projectId, CancellationToken ct)
    {
        // Candidate tasks are those Finance knows about at all: a task-scoped budget line, or a ledger
        // entry naming the task. Anything else has zero on both sides and cannot be over budget.
        List<Guid> fromBudgetLines = await db.BudgetLines
            .Where(b => b.ProjectId == projectId && b.IsActive && b.ProjectTaskId != null)
            .Select(b => b.ProjectTaskId!.Value)
            .Distinct()
            .ToListAsync(ct);

        List<Guid> fromLedger = await db.LedgerEntries
            .Where(e => e.ProjectId == projectId && e.IsActive && e.ProjectTaskId != null)
            .Select(e => e.ProjectTaskId!.Value)
            .Distinct()
            .ToListAsync(ct);

        Guid[] taskIds = fromBudgetLines.Concat(fromLedger).Distinct().ToArray();
        if (taskIds.Length == 0)
        {
            return [];
        }

        Dictionary<Guid, FinancialBlocks.TaskBlock> blocks = await FinancialBlocks.BuildTaskBlocksAsync(db, taskIds, ct);
        var names = (await projects.GetTasksAsync(taskIds, ct)).ToDictionary(t => t.Id, t => t.Name);

        return [.. blocks
            .Where(kv => kv.Value.Actual > kv.Value.EffectiveCap)
            .OrderByDescending(kv => kv.Value.Actual - kv.Value.EffectiveCap)
            .Select(kv => new OverBudgetTaskRow(
                kv.Key,
                names.GetValueOrDefault(kv.Key) ?? string.Empty,
                kv.Value.EffectiveCap,
                kv.Value.Actual,
                kv.Value.Actual - kv.Value.EffectiveCap))];
    }

    private Task<BoqSummary?> BoqSummaryAsync(Guid projectId, CancellationToken ct) =>
        db.BoqSnapshots
            .Where(b => b.ProjectId == projectId && b.IsActive)
            .OrderByDescending(b => b.Version)
            .Select(b => new BoqSummary(
                b.Id, b.Version, b.GeneratedAt,
                b.BudgetedTotal, b.PurchaseTotal, b.DifferenceTotal, b.ActualTotal))
            .FirstOrDefaultAsync(ct);

    private Task<List<RecentLedgerRow>> RecentActivityAsync(Guid projectId, int count, CancellationToken ct) =>
        db.LedgerEntries
            .Where(e => e.ProjectId == projectId && e.IsActive)
            .OrderByDescending(e => e.OccurredOn)
            .ThenByDescending(e => e.CreatedAtUtc)
            .Take(count)
            .Select(e => new RecentLedgerRow(
                e.Id, e.OccurredOn, e.Direction.ToString(), e.Type.ToString(),
                e.Amount, e.ClientId, e.VendorName, e.Reference))
            .ToListAsync(ct);
}
