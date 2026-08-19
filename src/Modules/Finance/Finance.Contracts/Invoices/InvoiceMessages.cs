using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;

namespace Finance.Contracts.Invoices;

// ── Requests ───────────────────────────────────────────────────────────────────

public sealed record InvoiceLineDto(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    string? Unit = null,
    Guid? ProjectTaskId = null,
    Guid? BudgetLineId = null);

public sealed record CreateInvoiceRequest(
    string Number,
    Guid ProjectId,
    InvoiceType Type,
    DateOnly IssuedOn,
    IReadOnlyCollection<InvoiceLineDto> Lines,
    Guid? ProjectStageId = null,
    DateOnly? DueOn = null,
    Guid? ClientId = null,
    string? VendorName = null,
    string? Note = null);

public sealed record AddInvoiceLineRequest(InvoiceLineDto Line);

// ── Responses ──────────────────────────────────────────────────────────────────

public sealed record InvoiceLineResponse(
    Guid Id,
    Guid InvoiceId,
    Guid? ProjectTaskId,
    Guid? BudgetLineId,
    string Description,
    decimal Quantity,
    string? Unit,
    decimal UnitPrice,
    decimal LineTotal);

/// <summary><see cref="Balance"/> is derived — <c>TotalAmount - PaidAmount</c>
/// (<c>../backend/Domain/Entities/Invoice.cs:30</c>) — and projected rather than left to the
/// caller.</summary>
public sealed record InvoiceResponse(
    Guid Id,
    string Number,
    Guid ProjectId,
    Guid? ProjectStageId,
    string Type,
    Guid? ClientId,
    string? VendorName,
    DateOnly IssuedOn,
    DateOnly? DueOn,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal Balance,
    string Status,
    string? Note,
    IReadOnlyList<InvoiceLineResponse> Lines,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

/// <summary>
/// The invoice-scoped list row. <b>Distinct from <c>Finance.Contracts.Financial.InvoiceListItemResponse</c></b>,
/// which is a different shape serving the project/stage drilldowns — the backend declares two records
/// under one name in two namespaces, and the per-noun namespaces here preserve that rather than
/// silently merging two wire contracts. See the note on the Financial one.
/// </summary>
public sealed record InvoiceListItemResponse(
    Guid Id,
    string Number,
    Guid ProjectId,
    string Type,
    string Status,
    DateOnly IssuedOn,
    DateOnly? DueOn,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal Balance,
    Guid? ClientId,
    string? VendorName);

// ── Commands ───────────────────────────────────────────────────────────────────

/// <summary>Creation <b>is</b> issuance — the backend raises <c>InvoiceIssuedDomainEvent</c> here and
/// has no separate issue step (<c>InvoiceDomainService.cs:48</c>). The header total is computed from
/// the lines, never supplied by the caller.</summary>
public sealed record CreateInvoiceCommand(CreateInvoiceRequest Request) : ICommand<Guid>;

public sealed record AddInvoiceLineCommand(Guid InvoiceId, InvoiceLineDto Line) : ICommand;

public sealed record CancelInvoiceCommand(Guid Id) : ICommand;

// ── Queries ────────────────────────────────────────────────────────────────────

public sealed record GetInvoiceByIdQuery(Guid Id) : IQuery<InvoiceResponse>;

public sealed record GetInvoicesByProjectQuery(
    Guid ProjectId,
    InvoiceStatus? Status = null,
    int Skip = 0,
    int Take = 50) : IQuery<PagedResult<InvoiceListItemResponse>>;

public sealed record GetOutstandingInvoicesQuery(Guid ProjectId) : IQuery<IReadOnlyList<InvoiceListItemResponse>>;
