using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;

namespace Finance.Contracts.LedgerEntries;

// ── Requests ───────────────────────────────────────────────────────────────────

public sealed record PostLedgerEntryRequest(
    Guid ProjectId,
    decimal Amount,
    LedgerDirection Direction,
    LedgerEntryType Type,
    DateOnly OccurredOn,
    Guid? ProjectStageId = null,
    Guid? ProjectTaskId = null,
    Guid? BudgetLineId = null,
    PaymentMethod? Method = null,
    Guid? ClientId = null,
    string? VendorName = null,
    Guid? VariationId = null,
    string? Reference = null,
    string? Note = null);

public sealed record ReverseLedgerEntryRequest(string? Note = null);

public sealed record AllocateLedgerEntryRequest(Guid InvoiceId, decimal Amount);

// ── Responses ──────────────────────────────────────────────────────────────────

public sealed record LedgerEntryResponse(
    Guid Id,
    Guid ProjectId,
    Guid? ProjectStageId,
    Guid? ProjectTaskId,
    Guid? BudgetLineId,
    string Direction,
    string Type,
    decimal Amount,
    string? Method,
    Guid? ClientId,
    string? VendorName,
    Guid? VariationId,
    Guid? ReversalOfId,
    DateOnly OccurredOn,
    string? Reference,
    string? Note,
    Guid RecordedById,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

/// <summary><see cref="IsReversed"/>: a reversal of this entry exists (the entry has been reversed, not "is a
/// reversal").</summary>
public sealed record LedgerEntryListItemResponse(
    Guid Id,
    Guid ProjectId,
    Guid? ProjectStageId,
    Guid? ProjectTaskId,
    string Direction,
    string Type,
    decimal Amount,
    DateOnly OccurredOn,
    Guid? ClientId,
    string? VendorName,
    string? Reference,
    bool IsReversed,
    string? Note);

/// <summary><see cref="UnallocatedAmount"/> is <see cref="Amount"/> less the sum of this entry's
/// allocations — the ceiling any further allocation is bounded by.</summary>
public sealed record UnallocatedLedgerEntryResponse(
    Guid Id,
    Guid ProjectId,
    decimal Amount,
    decimal UnallocatedAmount,
    DateOnly OccurredOn,
    Guid? ClientId,
    string? VendorName,
    string? Reference);

// ── Commands ───────────────────────────────────────────────────────────────────

public sealed record PostLedgerEntryCommand(PostLedgerEntryRequest Request, Guid RecordedById) : ICommand<Guid>;

/// <summary>Writes a mirror entry with the direction flipped and <c>ReversalOfId</c> set, then replays
/// the original posting's budget-line effects with the sign negated. Returns the new entry's id.</summary>
public sealed record ReverseLedgerEntryCommand(Guid Id, Guid RecordedById, string? Note = null) : ICommand<Guid>;

public sealed record AllocateLedgerEntryCommand(Guid Id, Guid InvoiceId, decimal Amount, Guid ActorId) : ICommand;

// ── Queries ────────────────────────────────────────────────────────────────────

public sealed record GetLedgerEntryByIdQuery(Guid Id) : IQuery<LedgerEntryResponse>;

public sealed record GetLedgerEntriesByProjectQuery(
    Guid ProjectId,
    LedgerDirection? Direction = null,
    LedgerEntryType? Type = null,
    Guid? ProjectStageId = null,
    Guid? ProjectTaskId = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    int Skip = 0,
    int Take = 50) : IQuery<PagedResult<LedgerEntryListItemResponse>>;

public sealed record GetUnallocatedLedgerEntriesQuery(
    Guid ProjectId,
    int Skip = 0,
    int Take = 50) : IQuery<PagedResult<UnallocatedLedgerEntryResponse>>;
