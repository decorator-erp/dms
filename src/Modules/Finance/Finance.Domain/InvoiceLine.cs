using SharedKernel;

namespace Finance.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/InvoiceLine.cs</c>. No tax:
/// <see cref="LineTotal"/> = <see cref="Quantity"/> × <see cref="UnitPrice"/>.
///
/// <para><b>This is the only place in the entire Finance area that rounds.</b> BOQ header totals,
/// variation application, invoice header totals and every ledger delta are raw sums — verified by
/// reading each site, not assumed. The rounding rule is recorded on <see cref="Create"/>.</para>
/// </summary>
internal sealed class InvoiceLine : Entity, IAuditable
{
    public Guid InvoiceId { get; private set; }

    /// <summary>Bare Guid → <c>projects.project_tasks</c>.</summary>
    public Guid? ProjectTaskId { get; private set; }

    public Guid? BudgetLineId { get; private set; }

    public string Description { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public string? Unit { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal LineTotal { get; private set; }

    private InvoiceLine()
    {
        // EF materialization.
    }

    /// <summary>
    /// <para><b>The rounding rule, ported verbatim:</b>
    /// <c>LineTotal = Math.Round(quantity * unitPrice, 4)</c> — scale 4, matching the
    /// <c>numeric(19,4)</c> column, using .NET's default <see cref="MidpointRounding.ToEven"/>
    /// (banker's rounding). <c>MidpointRounding</c> appears nowhere in the backend, so the default is
    /// the rule rather than an oversight to correct; a midpoint such as <c>0.00005</c> rounds to the
    /// even digit, not away from zero. Changing this to <c>AwayFromZero</c> would be a silent
    /// half-cent divergence on every midpoint line, which is the class of bug that surfaces as a
    /// one-cent discrepancy six months in.</para>
    /// </summary>
    public static Result<InvoiceLine> Create(
        Guid invoiceId,
        string description,
        decimal quantity,
        decimal unitPrice,
        string? unit = null,
        Guid? projectTaskId = null,
        Guid? budgetLineId = null)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return InvoiceLineErrors.DescriptionRequired;
        }

        if (quantity <= 0)
        {
            return InvoiceLineErrors.InvalidQuantity;
        }

        // DIVERGENCE (backend defect). The backend guards quantity but not unit price, so a negative
        // price produces a negative LineTotal, silently reducing the invoice header total and, through
        // the BOQ purchase total, the project's reported spend. Zero stays legal - a no-charge line is
        // a real thing.
        if (unitPrice < 0)
        {
            return InvoiceLineErrors.InvalidUnitPrice;
        }

        return new InvoiceLine
        {
            InvoiceId = invoiceId,
            Description = description,
            Quantity = quantity,
            Unit = unit,
            UnitPrice = unitPrice,
            LineTotal = Math.Round(quantity * unitPrice, 4),
            ProjectTaskId = projectTaskId,
            BudgetLineId = budgetLineId,
        };
    }
}
