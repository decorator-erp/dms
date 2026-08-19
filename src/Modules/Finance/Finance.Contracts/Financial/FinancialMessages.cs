using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;

namespace Finance.Contracts.Financial;

// ═══════════════════════════════════════════════════════════════════════════════
// The project/stage/task financial read slice.
//
// These twelve queries back the three Project*Financial endpoint families, whose ROUTES live under
// /api/projects, /api/project-stages and /api/project-tasks but whose HANDLERS are Finance's. Route
// shape and module ownership are independent; this is the first place in the system where they
// visibly diverge (written up in docs/adding-a-module.md at F2-01).
//
// In the backend these files sit under Application/Features/{Projects,ProjectStages,ProjectTasks}/,
// which is why an inventory counting only the seven finance noun folders found 39 feature files
// instead of 51. They are Finance's by data ownership, and they port here.
//
// Vocabulary, mirroring the entity model:
//   EffectiveCap    = EstimatedBudget + ApprovedAdjustments
//   AvailableAmount = EffectiveCap - CommittedAmount - ActualAmount
// CommittedAmount and ActualAmount are signed-sum aggregates over LedgerEntry, with commitments
// consumed by actuals.
// ═══════════════════════════════════════════════════════════════════════════════

// ── Shared read DTOs ───────────────────────────────────────────────────────────

public sealed record LedgerMovementResponse(
    Guid Id,
    DateOnly OccurredOn,
    LedgerEntryType Type,
    LedgerDirection Direction,
    decimal Amount,
    Guid ProjectId,
    Guid? ProjectStageId,
    Guid? ProjectTaskId,
    Guid? BudgetLineId,
    string? BudgetLineName,
    Guid? VariationId,
    Guid? ReversalOfId,
    string? VendorName,
    Guid? ClientId,
    string? Reference,
    string? Note);

/// <summary>
/// The drilldown invoice row. <b>Deliberately a different record from
/// <c>Finance.Contracts.Invoices.InvoiceListItemResponse</c></b>, which the backend also declares under
/// this name in another namespace: this one exposes real enum members and a <see cref="LinesCount"/>,
/// that one exposes stringified enums and a <c>Number</c>/<c>ProjectId</c> pair. Two wire contracts,
/// two namespaces, preserved rather than merged — merging them would change one of the two existing
/// response shapes.
/// </summary>
public sealed record InvoiceListItemResponse(
    Guid Id,
    string Number,
    InvoiceType Type,
    InvoiceStatus Status,
    DateOnly IssuedOn,
    DateOnly? DueOn,
    Guid? ProjectStageId,
    Guid? ClientId,
    string? VendorName,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal Balance,
    int LinesCount);

public sealed record BoqSnapshotSummaryResponse(
    Guid Id,
    int Version,
    BoqSnapshotType Type,
    DateTime GeneratedAt,
    decimal BudgetedTotal,
    decimal PurchaseTotal,
    decimal DifferenceTotal,
    decimal ActualTotal,
    int LinesCount);

/// <summary><b>Distinct from <c>Finance.Contracts.Variations.VariationListItemResponse</c></b> — same
/// reasoning as <see cref="InvoiceListItemResponse"/> above.</summary>
public sealed record VariationListItemResponse(
    Guid Id,
    VariationKind Kind,
    VariationStatus Status,
    Guid? ProjectStageId,
    Guid? ProjectTaskId,
    Guid? BudgetLineId,
    decimal Amount,
    string Reason,
    DateTime? ApprovedAt,
    DateTime? SignedAt);

// REMOVED AT F1-07b: ProjectTaskFinancialBlock and ProjectStageFinancialBlock.
//
// F1-01 shipped both, following the backend's DTO set. Porting the queries showed they have NO consumer
// here: in the backend they are returned only by GetProjectById / GetProjectDetails /
// GetProjectStageById / GetProjectTaskById / GetProjectTaskDetails / GetProjectTasksByStage -- all
// PROJECTS-owned detail queries that enrich a Projects response with a financial block. This port does
// not extend those, and none of the fifteen queries below returns either type.
//
// Two public records nothing produces is exactly the G9-14 shape, and keeping them would be keeping
// half of a decision this phase already made: structural item 3 declined to ship an IFinanceReader
// because it had no consumer. These are that contract's payload without its interface. The day Projects
// wants an enriched detail response, the right shape is IFinanceReader carrying these -- added then,
// with the consumer, in one commit.
//
// ProjectFinancialBlock and StageBudgetRow below STAY: GetProjectFinancialSummaryQuery returns both.

public sealed record ProjectFinancialBlock(
    string Currency,
    decimal EstimatedBudget,
    decimal ApprovedAdjustments,
    decimal EffectiveCap,
    decimal AllocatedAmount,
    decimal CommittedAmount,
    decimal ActualAmount,
    decimal AvailableAmount,
    decimal ClientPaidAmount,
    decimal VendorPaidAmount,
    decimal CollectionPercent,
    decimal CostPercent,
    decimal Earnings);

public sealed record StageBudgetRow(
    Guid ProjectStageId,
    string Name,
    int Order,
    decimal EstimatedBudget,
    decimal ApprovedAdjustments,
    decimal EffectiveCap,
    decimal AllocatedAmount,
    decimal CommittedAmount,
    decimal ActualAmount,
    decimal AvailableAmount,
    decimal RequiredBudget,
    decimal ClientPaidAmount);

public sealed record CostCategoryRow(
    Guid? CostCategoryId,
    string CategoryName,
    decimal AllocatedAmount,
    decimal CommittedAmount,
    decimal ActualAmount);

public sealed record MonthlyCashRow(int Year, int Month, decimal Inflow, decimal Outflow);

public sealed record ProjectFinancialSummaryResponse(
    ProjectFinancialBlock Totals,
    IReadOnlyList<StageBudgetRow> Stages);

// ── Task-scope drilldown rows ──────────────────────────────────────────────────

public sealed record TaskInvoiceLineResponse(
    Guid InvoiceId,
    string InvoiceNumber,
    InvoiceType InvoiceType,
    InvoiceStatus InvoiceStatus,
    DateOnly IssuedOn,
    Guid InvoiceLineId,
    string Description,
    decimal Quantity,
    string? Unit,
    decimal UnitPrice,
    decimal LineTotal,
    Guid? BudgetLineId);

public sealed record TaskBoqLineResponse(
    Guid BoqSnapshotId,
    int SnapshotVersion,
    DateTime GeneratedAt,
    BoqSnapshotType SnapshotType,
    Guid BoqSnapshotLineId,
    string Description,
    decimal? Quantity,
    string? Unit,
    decimal BudgetedAmount,
    decimal ActualAmount,
    decimal DifferenceAmount,
    Guid? VariationId,
    Guid? InvoiceLineId);

// ── Project-scope queries ──────────────────────────────────────────────────────

public sealed record GetProjectFinancialSummaryQuery(Guid ProjectId) : IQuery<ProjectFinancialSummaryResponse>;

public sealed record GetProjectFinancialMovementsQuery(
    Guid ProjectId,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    LedgerEntryType? Type = null,
    LedgerDirection? Direction = null,
    Guid? ProjectStageId = null,
    Guid? ProjectTaskId = null,
    Guid? BudgetLineId = null,
    Guid? VariationId = null,
    int Skip = 0,
    int Take = 50) : IQuery<PagedResult<LedgerMovementResponse>>;

/// <summary>Backs <c>GET /api/projects/{id}/financial/by-category</c>.</summary>
public sealed record GetProjectCostCategoryBreakdownQuery(Guid ProjectId) : IQuery<IReadOnlyList<CostCategoryRow>>;

/// <summary>
/// Backs <c>GET /api/projects/{id}/financial/cashflow</c>.
///
/// <para>The date range was added at F1-07b. F1-01 recorded, from the phase plan, that this and
/// <see cref="GetProjectCostCategoryBreakdownQuery"/> "resolve through <c>FinancialBlocks</c> rather
/// than their own query file" — the plan flagged it as needing confirmation while porting, and it is
/// <b>wrong</b>: both have real query files in the backend
/// (<c>GetProjectMonthlyCashflowQuery</c>, <c>GetProjectCostBreakdownByCategoryQuery</c>), and the
/// cashflow one takes a window. Defaults to the trailing twelve months.</para>
/// </summary>
public sealed record GetProjectCashflowQuery(
    Guid ProjectId,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null) : IQuery<IReadOnlyList<MonthlyCashRow>>;

public sealed record GetProjectInvoicesQuery(
    Guid ProjectId,
    InvoiceStatus? Status = null,
    InvoiceType? Type = null,
    Guid? ProjectStageId = null,
    int Skip = 0,
    int Take = 50) : IQuery<PagedResult<InvoiceListItemResponse>>;

public sealed record GetProjectVariationsQuery(
    Guid ProjectId,
    VariationStatus? Status = null,
    VariationKind? Kind = null,
    Guid? ProjectStageId = null,
    Guid? ProjectTaskId = null,
    int Skip = 0,
    int Take = 50) : IQuery<PagedResult<VariationListItemResponse>>;

public sealed record GetProjectBoqSnapshotsQuery(
    Guid ProjectId,
    BoqSnapshotType? Type = null,
    int? Version = null) : IQuery<IReadOnlyList<BoqSnapshotSummaryResponse>>;

// ── Stage-scope queries ────────────────────────────────────────────────────────

public sealed record GetStageFinancialMovementsQuery(
    Guid ProjectStageId,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    LedgerEntryType? Type = null) : IQuery<IReadOnlyList<LedgerMovementResponse>>;

public sealed record GetStageInvoicesQuery(
    Guid ProjectStageId,
    InvoiceStatus? Status = null,
    InvoiceType? Type = null) : IQuery<IReadOnlyList<InvoiceListItemResponse>>;

public sealed record GetStageVariationsQuery(Guid ProjectStageId, VariationStatus? Status = null)
    : IQuery<IReadOnlyList<VariationListItemResponse>>;

public sealed record GetStageBoqSnapshotsQuery(Guid ProjectStageId, BoqSnapshotType? Type = null)
    : IQuery<IReadOnlyList<BoqSnapshotSummaryResponse>>;

// ── Task-scope queries ─────────────────────────────────────────────────────────

public sealed record GetTaskFinancialMovementsQuery(
    Guid ProjectTaskId,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    LedgerEntryType? Type = null) : IQuery<IReadOnlyList<LedgerMovementResponse>>;

public sealed record GetTaskInvoiceLinesQuery(Guid ProjectTaskId) : IQuery<IReadOnlyList<TaskInvoiceLineResponse>>;

/// <summary>Backs <c>GET /api/project-tasks/{id}/boq-lines</c>. Without
/// <paramref name="SnapshotVersion"/> it returns the task's lines from the LATEST snapshot of each
/// type; naming a version overrides that and returns only that version's. The parameter was added at
/// F1-07b — the backend has it and F1-01 missed it.</summary>
public sealed record GetTaskBoqLinesQuery(Guid ProjectTaskId, int? SnapshotVersion = null)
    : IQuery<IReadOnlyList<TaskBoqLineResponse>>;

public sealed record GetTaskVariationsQuery(Guid ProjectTaskId, VariationStatus? Status = null)
    : IQuery<IReadOnlyList<VariationListItemResponse>>;
