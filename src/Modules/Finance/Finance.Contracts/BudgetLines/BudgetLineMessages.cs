using BuildingBlocks.Messaging;

namespace Finance.Contracts.BudgetLines;

// ── Requests ───────────────────────────────────────────────────────────────────

public sealed record CreateBudgetLineRequest(
    string Code,
    Guid ProjectId,
    Guid CostCategoryId,
    string Name,
    decimal AllocatedAmount,
    Guid? ProjectStageId = null,
    Guid? ProjectTaskId = null,
    string? Description = null);

public sealed record UpdateBudgetLineAllocationRequest(decimal AllocatedAmount);

// ── Responses ──────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="EffectiveAllocation"/> and <see cref="AvailableAmount"/> are <b>derived</b>, not stored:
/// <c>EffectiveAllocation = AllocatedAmount + AdjustmentAllocation</c> and
/// <c>AvailableAmount = EffectiveAllocation - CommittedAmount - ActualAmount</c> (see
/// <c>../backend/Domain/Entities/BudgetLine.cs:29,31</c>). They are projected here so a caller never
/// re-derives them and drifts from the definition the ledger enforces.
/// </summary>
public sealed record BudgetLineResponse(
    Guid Id,
    string Code,
    Guid ProjectId,
    Guid CostCategoryId,
    Guid? ProjectStageId,
    Guid? ProjectTaskId,
    string Name,
    string? Description,
    decimal AllocatedAmount,
    decimal AdjustmentAllocation,
    decimal EffectiveAllocation,
    decimal CommittedAmount,
    decimal ActualAmount,
    decimal AvailableAmount,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    string? CostCategoryName);

public sealed record BudgetLineListItemResponse(
    Guid Id,
    string Code,
    Guid ProjectId,
    Guid? CostCategoryId,
    Guid? ProjectStageId,
    Guid? ProjectTaskId,
    string Name,
    decimal AllocatedAmount,
    decimal AdjustmentAllocation,
    decimal EffectiveAllocation,
    decimal CommittedAmount,
    decimal ActualAmount,
    decimal AvailableAmount,
    string? CostCategoryName);

// ── Commands ───────────────────────────────────────────────────────────────────

public sealed record CreateBudgetLineCommand(CreateBudgetLineRequest Request) : ICommand<Guid>;

/// <summary>Re-derives <c>CommittedAmount</c>/<c>ActualAmount</c> from a full re-sum of the ledger,
/// rather than trusting the running deltas. The backend's repair hatch, ported as-is.</summary>
public sealed record RecalculateBudgetLineCommand(Guid Id) : ICommand;

public sealed record UpdateBudgetLineAllocationCommand(Guid Id, decimal AllocatedAmount) : ICommand;

// ── Queries ────────────────────────────────────────────────────────────────────

public sealed record GetBudgetLineByIdQuery(Guid Id) : IQuery<BudgetLineResponse>;

public sealed record GetBudgetLinesByProjectQuery(
    Guid ProjectId,
    Guid? CostCategoryId = null,
    Guid? ProjectStageId = null,
    Guid? ProjectTaskId = null) : IQuery<IReadOnlyList<BudgetLineListItemResponse>>;
