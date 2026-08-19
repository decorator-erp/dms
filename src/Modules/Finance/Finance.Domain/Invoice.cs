using SharedKernel;

namespace Finance.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/Invoice.cs</c>. A client or vendor invoice.
///
/// <para><b>No tax, no retention</b> — final amounts only, matching the backend. Tax/VAT computation is
/// a recorded product decision left out of Phase 7.</para>
///
/// <para><see cref="TotalAmount"/> is cached from the lines and <see cref="PaidAmount"/> from the
/// settling ledger allocations; neither is set by a caller directly.</para>
/// </summary>
internal sealed class Invoice : Entity, IAuditable
{
    /// <summary>The human-facing natural key, and what the duplicate check keys on.</summary>
    public string Number { get; private set; } = string.Empty;

    /// <summary>Bare Guid → <c>projects.projects</c>.</summary>
    public Guid ProjectId { get; private set; }

    public Guid? ProjectStageId { get; private set; }

    public InvoiceType Type { get; private set; }

    /// <summary>Bare Guid → <c>crm.clients</c>. Required for a <see cref="InvoiceType.ClientInvoice"/>.</summary>
    public Guid? ClientId { get; private set; }

    /// <summary>Free text, required for a <see cref="InvoiceType.VendorInvoice"/>. A vendor has no user
    /// account, which is why F1-08's invoice-due reminder can never reach one.</summary>
    public string? VendorName { get; private set; }

    public DateOnly IssuedOn { get; private set; }
    public DateOnly? DueOn { get; private set; }

    public decimal TotalAmount { get; private set; }
    public decimal PaidAmount { get; private set; }

    public InvoiceStatus Status { get; private set; }
    public string? Note { get; private set; }

    public decimal Balance => TotalAmount - PaidAmount;

    public bool IsPayable => Status is InvoiceStatus.Open;

    private Invoice()
    {
        // EF materialization.
    }

    public static Result<Invoice> Create(
        string number,
        Guid projectId,
        InvoiceType type,
        DateOnly issuedOn,
        Guid? projectStageId = null,
        DateOnly? dueOn = null,
        Guid? clientId = null,
        string? vendorName = null,
        string? note = null)
    {
        // DIVERGENCE (backend defect). The backend's Create never checks the number, leaving it to a
        // FluentValidation rule at the edge -- so any handler constructing an invoice directly can
        // persist one with an empty natural key, which is also what DuplicatedNumber keys on.
        if (string.IsNullOrWhiteSpace(number))
            return InvoiceErrors.NumberRequired;

        var invoice = new Invoice
        {
            Number = number,
            ProjectId = projectId,
            Type = type,
            Status = InvoiceStatus.Open,
            IssuedOn = issuedOn,
            DueOn = dueOn,
            ClientId = clientId,
            VendorName = vendorName,
            Note = note,
            ProjectStageId = projectStageId,
        };

        return ValidateType(invoice) is { IsFailure: true } typeResult ? typeResult.Error : invoice;
    }

    /// <summary>Sets the header total from the sum of the lines. Never caller-supplied — the whole point
    /// is that the header cannot disagree with its lines.</summary>
    public void SetTotal(decimal totalAmount) => TotalAmount = totalAmount;

    /// <summary>
    /// Settles <paramref name="amount"/> against this invoice, flipping to
    /// <see cref="InvoiceStatus.Paid"/> once the balance reaches zero.
    ///
    /// <para>Bounded by <see cref="Balance"/>, so an invoice can never be over-paid; the caller's own
    /// allocation bound (<see cref="LedgerAllocation.WouldExceedUnallocated"/>) bounds the same money
    /// from the payment's side. Both are needed — one stops over-paying an invoice, the other stops
    /// over-spending a payment.</para>
    /// </summary>
    public Result ApplyPayment(decimal amount)
    {
        if (!IsPayable)
            return InvoiceErrors.NotPayable;

        if (amount <= 0 || amount > Balance)
            return InvoiceErrors.OverAllocation;

        PaidAmount += amount;

        if (Balance <= 0)
            Status = InvoiceStatus.Paid;

        return Result.Success();
    }

    /// <summary>Cancellable only while untouched: not already paid or cancelled, and with nothing
    /// allocated against it. A part-paid invoice must have its payments reversed first, so the ledger
    /// and the invoice can never disagree about money already received.</summary>
    public Result Cancel()
    {
        if (Status is InvoiceStatus.Paid or InvoiceStatus.Cancelled || PaidAmount > 0)
            return InvoiceErrors.InvalidStatusTransition;

        Status = InvoiceStatus.Cancelled;
        return Result.Success();
    }

    private static Result ValidateType(Invoice invoice) => invoice.Type switch
    {
        InvoiceType.ClientInvoice when invoice.ClientId is null => InvoiceErrors.ClientRequired,
        InvoiceType.VendorInvoice when string.IsNullOrWhiteSpace(invoice.VendorName) => InvoiceErrors.VendorRequired,
        _ => Result.Success(),
    };
}
