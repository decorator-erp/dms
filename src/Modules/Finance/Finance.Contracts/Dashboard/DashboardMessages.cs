using BuildingBlocks.Messaging;

namespace Finance.Contracts.Dashboard;

/// <summary>
/// <see cref="Currency"/> is a single configured value, not a per-row currency code.
/// <b>Multi-currency and FX are explicitly out of scope for Phase 7</b> — a recorded product decision,
/// not an oversight. Every amount in this module is in one currency; introducing a second would need
/// a rate table, a conversion point, and a decision about which rate a historical row is valued at,
/// none of which the backend models either.
/// </summary>
public sealed record ProjectFinancialDashboardResponse(
    Guid ProjectId,
    string Currency,
    BudgetSummary Budget,
    CollectionSummary Collection,
    EarningsSummary Earnings,
    IReadOnlyList<BudgetLineRow> BudgetLines,
    InvoiceSummary Invoices,
    VariationSummary Variations,
    IReadOnlyList<OverBudgetTaskRow> OverBudgetTasks,
    BoqSummary? Boq,
    IReadOnlyList<RecentLedgerRow> RecentActivity);

public sealed record BudgetSummary(
    decimal EstimatedBudget,
    decimal ApprovedAdjustments,
    decimal EffectiveCap,
    decimal? ActualBudget,
    decimal TotalAllocated,
    decimal TotalCommitted,
    decimal TotalActual,
    decimal TotalAvailable);

/// <summary><see cref="RequiredBudget"/> and <see cref="ClientPaidAmount"/> are <b>derived</b>, not
/// columns. The backend removed the stored versions and computes them from client invoices and client
/// payments (<c>../backend/Domain/Services/Snapshots.cs:70-74</c>); <c>IsFunded</c> is
/// <c>ClientInvoicesSum &gt; 0 &amp;&amp; ClientPaymentsSum &gt;= ClientInvoicesSum</c>. A doc comment
/// on the backend's own <c>BudgetHandler</c> still refers to a <c>RequiredBudget</c> column that no
/// longer exists — do not reintroduce it.</summary>
public sealed record StageFundingRow(
    Guid StageId,
    string Name,
    decimal RequiredBudget,
    decimal ClientPaidAmount,
    bool IsFunded);

public sealed record CollectionSummary(
    decimal TotalClientPayments,
    decimal CollectionPercentOfEstimatedBudget,
    IReadOnlyList<StageFundingRow> Stages);

public sealed record EarningsSummary(
    decimal TotalIn,
    decimal TotalOut,
    decimal Net,
    decimal MarginPercent);

public sealed record BudgetLineRow(
    Guid BudgetLineId,
    Guid? CostCategoryId,
    string? CostCategoryName,
    string BudgetLineName,
    decimal Allocated,
    decimal Adjustment,
    decimal Committed,
    decimal Actual,
    decimal Available);

public sealed record InvoiceStatusBucket(string Status, int Count, decimal Total);

public sealed record InvoiceSummary(
    int TotalCount,
    decimal TotalOutstanding,
    IReadOnlyList<InvoiceStatusBucket> ByStatus);

public sealed record VariationBucket(string Kind, string Status, int Count, decimal Total);

public sealed record VariationSummary(
    int TotalCount,
    decimal SignedClientDifferenceTotal,
    IReadOnlyList<VariationBucket> ByKindAndStatus);

/// <summary><see cref="Overrun"/> is floored at zero — <c>Math.Max(0, ActualCost -
/// EffectiveAllocation)</c> — so an under-budget task reports 0, never a negative overrun. This is the
/// same expression the Budget-gate coverage check uses, and the two must not drift.</summary>
public sealed record OverBudgetTaskRow(
    Guid TaskId,
    string Name,
    decimal Allocated,
    decimal Actual,
    decimal Overrun);

public sealed record BoqSummary(
    Guid SnapshotId,
    int Version,
    DateTime GeneratedAt,
    decimal BudgetedTotal,
    decimal PurchaseTotal,
    decimal DifferenceTotal,
    decimal ActualTotal);

public sealed record RecentLedgerRow(
    Guid LedgerEntryId,
    DateOnly OccurredOn,
    string Direction,
    string Type,
    decimal Amount,
    Guid? ClientId,
    string? VendorName,
    string? Reference);

// ── Query ──────────────────────────────────────────────────────────────────────

public sealed record GetProjectFinancialDashboardQuery(Guid ProjectId, int RecentActivityCount = 20)
    : IQuery<ProjectFinancialDashboardResponse>;
