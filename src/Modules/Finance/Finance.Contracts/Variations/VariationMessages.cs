using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;

namespace Finance.Contracts.Variations;

// ── Requests ───────────────────────────────────────────────────────────────────

/// <summary>
/// The two <see cref="VariationKind"/>s populate disjoint halves of this request, and the validator
/// enforces which (see <c>RaiseVariationCommandValidator</c> in the backend):
/// <c>ClientDifference</c> requires <see cref="ProjectTaskId"/>, <see cref="BudgetedAmount"/> and
/// <see cref="ChosenAmount"/> and derives <c>Amount = Chosen - Budgeted</c>; <c>InternalChange</c>
/// requires <see cref="BudgetLineId"/> and <see cref="Amount"/>.
/// </summary>
public sealed record RaiseVariationRequest(
    VariationKind Kind,
    Guid ProjectId,
    string Reason,
    Guid? ProjectTaskId = null,
    Guid? ProjectStageId = null,
    Guid? BudgetLineId = null,
    decimal? BudgetedAmount = null,
    decimal? ChosenAmount = null,
    decimal? Amount = null,
    string? Description = null);

public sealed record ApproveVariationRequest(Guid? SignedByClientId = null, string? SignedDocumentPath = null);

public sealed record SignVariationRequest(Guid SignedByClientId, string SignedDocumentPath);

public sealed record RejectVariationRequest(string? Note = null);

public sealed record CancelVariationRequest(string? Note = null);

// ── Responses ──────────────────────────────────────────────────────────────────

public sealed record VariationResponse(
    Guid Id,
    string Kind,
    Guid ProjectId,
    Guid? ProjectTaskId,
    Guid? ProjectStageId,
    Guid? BudgetLineId,
    string Reason,
    string? Description,
    decimal Amount,
    decimal? BudgetedAmount,
    decimal? ChosenAmount,
    string Status,
    Guid RequestedById,
    Guid? ApprovedById,
    DateTime? ApprovedAt,
    DateTime? SentToClientAt,
    Guid? SignedByClientId,
    DateTime? SignedAt,
    string? SignedDocumentPath,
    string? ResolutionNote,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

/// <summary><b>Distinct from <c>Finance.Contracts.Financial.VariationListItemResponse</c></b> — same
/// name, different shape, two namespaces, exactly as the backend declares them.</summary>
public sealed record VariationListItemResponse(
    Guid Id,
    string Kind,
    Guid ProjectId,
    Guid? ProjectTaskId,
    Guid? ProjectStageId,
    decimal Amount,
    string Status,
    DateTime CreatedAtUtc);

// ── Commands ───────────────────────────────────────────────────────────────────

public sealed record RaiseVariationCommand(RaiseVariationRequest Request, Guid RequestedById) : ICommand<Guid>;

public sealed record SubmitVariationCommand(Guid Id) : ICommand;

public sealed record ApproveVariationCommand(
    Guid Id,
    Guid ApprovedById,
    Guid? SignedByClientId = null,
    string? SignedDocumentPath = null) : ICommand;

public sealed record SignVariationCommand(
    Guid Id,
    Guid ApprovedById,
    Guid SignedByClientId,
    string SignedDocumentPath) : ICommand;

/// <summary>
/// Terminal step. For <c>InternalChange</c> it pushes <c>Amount</c> into
/// <c>BudgetLine.AdjustmentAllocation</c> and marks the variation Applied in one transaction. For
/// <c>ClientDifference</c> it writes <b>nothing</b> — the signed delta stays on the variation row and
/// is read back by the task budget-coverage check
/// (<c>../backend/Domain/Services/VariationDomainService.cs:140-144</c>). That asymmetry is deliberate
/// and is the easiest thing in this module to "fix" into a bug.
/// </summary>
public sealed record ApplyVariationCommand(Guid Id, Guid RecordedById) : ICommand;

public sealed record RejectVariationCommand(Guid Id, Guid RejectedById, string? Note = null) : ICommand;

public sealed record CancelVariationCommand(Guid Id, string? Note = null) : ICommand;

// ── Queries ────────────────────────────────────────────────────────────────────

public sealed record GetVariationByIdQuery(Guid Id) : IQuery<VariationResponse>;

public sealed record GetVariationsByProjectQuery(
    Guid ProjectId,
    VariationKind? Kind = null,
    VariationStatus? Status = null,
    int Skip = 0,
    int Take = 50) : IQuery<PagedResult<VariationListItemResponse>>;

public sealed record GetVariationsByStageQuery(Guid StageId) : IQuery<IReadOnlyList<VariationListItemResponse>>;

public sealed record GetVariationsByTaskQuery(Guid TaskId) : IQuery<IReadOnlyList<VariationListItemResponse>>;
