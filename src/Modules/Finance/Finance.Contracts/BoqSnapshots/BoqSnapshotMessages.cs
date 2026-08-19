using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;

namespace Finance.Contracts.BoqSnapshots;

// ── Requests ───────────────────────────────────────────────────────────────────

public sealed record GenerateBoqSnapshotRequest(
    Guid ProjectId,
    BoqSnapshotType Type,
    Guid? ProjectStageId = null,
    string? Notes = null);

// ── Responses ──────────────────────────────────────────────────────────────────

/// <summary>
/// A line's <i>kind</i> is implicit in which id is set, not a stored column: <see cref="VariationId"/>
/// set means a Difference line, else <see cref="InvoiceLineId"/> set means a Purchase line, else it is
/// a BudgetItem line (<c>../backend/Domain/Entities/BoqSnapshotLine.cs:3-6</c>). Anything reading these
/// rows must apply that precedence in order — a line can carry both ids.
/// </summary>
public sealed record BoqSnapshotLineResponse(
    Guid Id,
    Guid BoqSnapshotId,
    string Description,
    decimal? Quantity,
    string? Unit,
    decimal BudgetedAmount,
    decimal ActualAmount,
    decimal DifferenceAmount,
    Guid? ProjectTaskId,
    Guid? InvoiceLineId,
    Guid? VariationId);

public sealed record BoqSnapshotResponse(
    Guid Id,
    Guid ProjectId,
    Guid? ProjectStageId,
    string Type,
    int Version,
    DateTime GeneratedAt,
    Guid GeneratedById,
    decimal BudgetedTotal,
    decimal PurchaseTotal,
    decimal DifferenceTotal,
    decimal ActualTotal,
    string? Notes,
    IReadOnlyList<BoqSnapshotLineResponse> Lines,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

public sealed record BoqSnapshotListItemResponse(
    Guid Id,
    Guid ProjectId,
    Guid? ProjectStageId,
    string Type,
    int Version,
    DateTime GeneratedAt,
    decimal BudgetedTotal,
    decimal PurchaseTotal,
    decimal DifferenceTotal,
    decimal ActualTotal);

// ── Commands ───────────────────────────────────────────────────────────────────

/// <summary>Snapshots are immutable and versioned per project; generating takes the next version under
/// a repeatable-read transaction. There is no update or delete command, by design.</summary>
public sealed record GenerateBoqSnapshotCommand(GenerateBoqSnapshotRequest Request, Guid GeneratedById) : ICommand<Guid>;

// ── Queries ────────────────────────────────────────────────────────────────────

public sealed record GetBoqSnapshotByIdQuery(Guid Id) : IQuery<BoqSnapshotResponse>;

public sealed record GetBoqSnapshotsByProjectQuery(
    Guid ProjectId,
    BoqSnapshotType? Type = null,
    int Skip = 0,
    int Take = 50) : IQuery<PagedResult<BoqSnapshotListItemResponse>>;

public sealed record GetLatestBoqSnapshotQuery(Guid ProjectId, Guid? ProjectStageId = null) : IQuery<BoqSnapshotResponse>;
