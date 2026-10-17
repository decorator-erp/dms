using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Finance.Application.Common;
using Finance.Contracts.Invoices;
using Finance.Domain;
using Finance.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Finance.Application.Features.Invoices;

internal sealed class CreateInvoiceCommandValidator : AbstractValidator<CreateInvoiceCommand>
{
    public CreateInvoiceCommandValidator()
    {
        RuleFor(x => x.Request.Number).NotEmpty().MaximumLength(48);
        RuleFor(x => x.Request.ProjectId).NotEmpty();
        RuleFor(x => x.Request.VendorName).MaximumLength(128);
        RuleFor(x => x.Request.Note).MaximumLength(512);

        RuleForEach(x => x.Request.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Description).NotEmpty().MaximumLength(256);
            line.RuleFor(l => l.Unit).MaximumLength(16);
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0);
        });

        // DIVERGENCE: the backend accepts a due date before the issue date, which produces an invoice
        // that is overdue the moment it exists and would be picked up by F1-08's due job on its first
        // tick. Nothing in the domain forbids it because Invoice.Create sees both as plain dates.
        RuleFor(x => x.Request)
            .Must(r => r.DueOn is null || r.DueOn >= r.IssuedOn)
            .WithMessage("DueOn cannot be earlier than IssuedOn.");
    }
}

/// <summary>
/// <b>Creation IS issuance</b> — there is no separate issue step, which is where the backend raises
/// its <c>InvoiceIssuedDomainEvent</c>.
///
/// <para><b>No integration event is published for it.</b> X-02 deleted the one that was: it had no
/// subscriber, and it could not have acquired a useful one without being fixed first — it fired only
/// here, while <c>AddInvoiceLineCommandHandler</c> re-derives <c>TotalAmount</c> and
/// <c>CancelInvoiceCommandHandler</c> changes status, neither publishing anything. A read model fed by
/// it would have drifted from the second line onward. The stage-funding republish below is a different
/// thing entirely and stays: it carries a decided verdict, and Projects consumes it.</para>
///
/// <para><b>The header total is computed from the lines and never supplied by the caller.</b> That is
/// the whole reason <c>SetTotal</c> takes an argument the caller cannot reach: an invoice whose header
/// disagrees with its lines is unauditable, and letting the caller name a total makes that the default
/// outcome rather than a bug.</para>
///
/// <para><b>DIVERGENCE: the project is validated.</b> Same shape as F1-06's two — the backend leaves an
/// unknown project to the foreign key, which surfaces as a 500 rather than a 404.</para>
/// </summary>
internal sealed class CreateInvoiceCommandHandler(FinanceDbContext db, IProjectDirectory projects)
    : ICommandHandler<CreateInvoiceCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateInvoiceCommand command, CancellationToken ct = default)
    {
        CreateInvoiceRequest r = command.Request;

        if (await db.Invoices.AnyAsync(i => i.Number == r.Number, ct))
        {
            return InvoiceErrors.DuplicatedNumber;
        }

        if (await projects.GetProjectAsync(r.ProjectId, ct) is null)
        {
            return InvoiceErrors.ProjectNotFound;
        }

        Result<Invoice> created = Invoice.Create(
            r.Number, r.ProjectId, FinanceEnumMapping.ToDomain(r.Type), r.IssuedOn,
            r.ProjectStageId, r.DueOn, r.ClientId, r.VendorName, r.Note);

        if (created.IsFailure)
        {
            return created.Error;
        }

        Invoice invoice = created.Value!;
        var lines = new List<InvoiceLine>();
        decimal total = 0m;

        foreach (InvoiceLineDto dto in r.Lines)
        {
            Result<InvoiceLine> line = InvoiceLine.Create(
                invoice.Id, dto.Description, dto.Quantity, dto.UnitPrice,
                dto.Unit, dto.ProjectTaskId, dto.BudgetLineId);

            if (line.IsFailure)
            {
                return line.Error;
            }

            lines.Add(line.Value!);
            total += line.Value!.LineTotal;
        }

        invoice.SetTotal(total);

        db.Invoices.Add(invoice);
        db.InvoiceLines.AddRange(lines);

        await db.SaveChangesAsync(ct);

        // A client invoice changes what a stage is owed, so it re-decides that stage's funding -- the
        // denominator of IsFunded, not just the numerator. Without this an invoice issued after a
        // payment leaves a stage reading as funded on stale figures until the next posting.
        if (invoice.Type == InvoiceType.ClientInvoice && invoice.ProjectStageId is Guid stageId)
        {
            await FinanceGateVerdicts.PublishAsync(db, projectTaskId: null, projectStageId: stageId, ct);
            await db.SaveChangesAsync(ct);
        }

        return invoice.Id;
    }
}

internal sealed class AddInvoiceLineCommandValidator : AbstractValidator<AddInvoiceLineCommand>
{
    public AddInvoiceLineCommandValidator()
    {
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.Line.Description).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Line.Unit).MaximumLength(16);
        RuleFor(x => x.Line.Quantity).GreaterThan(0);
        RuleFor(x => x.Line.UnitPrice).GreaterThanOrEqualTo(0);
    }
}

/// <summary>
/// Adds a line and re-derives the header from ALL lines rather than adding the new line's total to the
/// existing header. The backend does the same, and the difference matters: re-deriving is
/// self-correcting if the header was ever wrong, while incrementing preserves the error forever.
///
/// <para><b>DIVERGENCE: a paid or cancelled invoice cannot gain lines.</b> The backend allows it, which
/// silently raises <c>TotalAmount</c> above <c>PaidAmount</c> on an invoice already marked
/// <c>Paid</c> — leaving it Paid with a non-zero balance, a state nothing else in the module can
/// produce and no code path expects. Refused here with <c>InvalidStatusTransition</c>.</para>
/// </summary>
internal sealed class AddInvoiceLineCommandHandler(FinanceDbContext db)
    : ICommandHandler<AddInvoiceLineCommand>
{
    public async Task<Result> HandleAsync(AddInvoiceLineCommand command, CancellationToken ct = default)
    {
        Invoice? invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == command.InvoiceId && i.IsActive, ct);
        if (invoice is null)
        {
            return InvoiceErrors.NotFound;
        }

        if (!invoice.IsPayable)
        {
            return InvoiceErrors.InvalidStatusTransition;
        }

        Result<InvoiceLine> line = InvoiceLine.Create(
            command.InvoiceId, command.Line.Description, command.Line.Quantity, command.Line.UnitPrice,
            command.Line.Unit, command.Line.ProjectTaskId, command.Line.BudgetLineId);

        if (line.IsFailure)
        {
            return line.Error;
        }

        db.InvoiceLines.Add(line.Value!);
        await db.SaveChangesAsync(ct);

        decimal total = await db.InvoiceLines
            .Where(l => l.InvoiceId == command.InvoiceId)
            .SumAsync(l => (decimal?)l.LineTotal, ct) ?? 0m;

        invoice.SetTotal(total);
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}

/// <summary>Cancellable only while untouched — the domain refuses a part-paid invoice, so the ledger
/// and the invoice can never disagree about money already received. Reverse the payments first.</summary>
internal sealed class CancelInvoiceCommandHandler(FinanceDbContext db) : ICommandHandler<CancelInvoiceCommand>
{
    public async Task<Result> HandleAsync(CancelInvoiceCommand command, CancellationToken ct = default)
    {
        Invoice? invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == command.Id && i.IsActive, ct);
        if (invoice is null)
        {
            return InvoiceErrors.NotFound;
        }

        Result cancelled = invoice.Cancel();
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class GetInvoiceByIdQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetInvoiceByIdQuery, InvoiceResponse>
{
    public async Task<Result<InvoiceResponse>> HandleAsync(
        GetInvoiceByIdQuery query, CancellationToken ct = default)
    {
        var header = await db.Invoices
            .Where(i => i.Id == query.Id && i.IsActive)
            .Select(i => new
            {
                i.Id,
                i.Number,
                i.ProjectId,
                i.ProjectStageId,
                i.Type,
                i.ClientId,
                i.VendorName,
                i.IssuedOn,
                i.DueOn,
                i.TotalAmount,
                i.PaidAmount,
                i.Status,
                i.Note,
                i.CreatedAtUtc,
                i.UpdatedAtUtc,
            })
            .FirstOrDefaultAsync(ct);

        if (header is null)
        {
            return InvoiceErrors.NotFound;
        }

        List<InvoiceLineResponse> lines = await db.InvoiceLines
            .Where(l => l.InvoiceId == query.Id)
            .OrderBy(l => l.CreatedAtUtc)
            .Select(l => new InvoiceLineResponse(
                l.Id, l.InvoiceId, l.ProjectTaskId, l.BudgetLineId,
                l.Description, l.Quantity, l.Unit, l.UnitPrice, l.LineTotal))
            .ToListAsync(ct);

        return new InvoiceResponse(
            header.Id, header.Number, header.ProjectId, header.ProjectStageId, header.Type.ToString(),
            header.ClientId, header.VendorName, header.IssuedOn, header.DueOn,
            header.TotalAmount, header.PaidAmount, header.TotalAmount - header.PaidAmount,
            header.Status.ToString(), header.Note, lines, header.CreatedAtUtc, header.UpdatedAtUtc);
    }
}

internal sealed class GetInvoicesByProjectQueryValidator : AbstractValidator<GetInvoicesByProjectQuery>
{
    public GetInvoicesByProjectQueryValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 200);
    }
}

internal sealed class GetInvoicesByProjectQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetInvoicesByProjectQuery, PagedResult<InvoiceListItemResponse>>
{
    public async Task<Result<PagedResult<InvoiceListItemResponse>>> HandleAsync(
        GetInvoicesByProjectQuery query, CancellationToken ct = default)
    {
        IQueryable<Invoice> q = db.Invoices.Where(i => i.ProjectId == query.ProjectId && i.IsActive);

        if (query.Status is { } status)
        {
            InvoiceStatus value = FinanceEnumMapping.ToDomain(status);
            q = q.Where(i => i.Status == value);
        }

        int total = await q.CountAsync(ct);

        List<InvoiceListItemResponse> items = await q
            .OrderByDescending(i => i.IssuedOn)
            .ThenByDescending(i => i.CreatedAtUtc)
            .Skip(query.Skip)
            .Take(query.Take)
            .Select(i => new InvoiceListItemResponse(
                i.Id, i.Number, i.ProjectId, i.Type.ToString(), i.Status.ToString(),
                i.IssuedOn, i.DueOn, i.TotalAmount, i.PaidAmount, i.TotalAmount - i.PaidAmount,
                i.ClientId, i.VendorName))
            .ToListAsync(ct);

        return new PagedResult<InvoiceListItemResponse>(
            items, query.Skip / Math.Max(query.Take, 1), query.Take, total);
    }
}

/// <summary>Open invoices with a balance still to collect. Cancelled and fully-paid invoices are
/// excluded by the status filter; the balance filter additionally drops a zero-total open invoice,
/// which has nothing outstanding despite being open.</summary>
internal sealed class GetOutstandingInvoicesQueryValidator : AbstractValidator<GetOutstandingInvoicesQuery>
{
    public GetOutstandingInvoicesQueryValidator() => RuleFor(x => x.ProjectId).NotEmpty();
}

internal sealed class GetOutstandingInvoicesQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetOutstandingInvoicesQuery, IReadOnlyList<InvoiceListItemResponse>>
{
    public async Task<Result<IReadOnlyList<InvoiceListItemResponse>>> HandleAsync(
        GetOutstandingInvoicesQuery query, CancellationToken ct = default)
    {
        List<InvoiceListItemResponse> items = await db.Invoices
            .Where(i => i.ProjectId == query.ProjectId
                        && i.IsActive
                        && i.Status == InvoiceStatus.Open
                        && i.TotalAmount - i.PaidAmount > 0)
            .OrderBy(i => i.DueOn ?? DateOnly.MaxValue)
            .ThenBy(i => i.IssuedOn)
            .Select(i => new InvoiceListItemResponse(
                i.Id, i.Number, i.ProjectId, i.Type.ToString(), i.Status.ToString(),
                i.IssuedOn, i.DueOn, i.TotalAmount, i.PaidAmount, i.TotalAmount - i.PaidAmount,
                i.ClientId, i.VendorName))
            .ToListAsync(ct);

        return items;
    }
}
