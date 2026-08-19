using SharedKernel;

namespace Finance.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/BoqSnapshotLine.cs</c>.
///
/// <para><b>A line's kind is implicit in which source id is set — there is no kind column</b>, and the
/// precedence is ordered, not exclusive: <see cref="VariationId"/> set means Difference; otherwise
/// <see cref="InvoiceLineId"/> set means Purchase; otherwise BudgetItem. A line can legitimately carry
/// both ids, so a reader that tests <see cref="InvoiceLineId"/> first classifies difference lines as
/// purchases. <see cref="Kind"/> exists so nothing has to re-derive that order.</para>
/// </summary>
internal sealed class BoqSnapshotLine : Entity, IAuditable
{
    public Guid BoqSnapshotId { get; private set; }
    public string Description { get; private set; } = string.Empty;

    public decimal? Quantity { get; private set; }
    public string? Unit { get; private set; }

    public decimal BudgetedAmount { get; private set; }
    public decimal ActualAmount { get; private set; }
    public decimal DifferenceAmount { get; private set; }

    /// <summary>Bare Guid → <c>projects.project_tasks</c>.</summary>
    public Guid? ProjectTaskId { get; private set; }

    public Guid? InvoiceLineId { get; private set; }
    public Guid? VariationId { get; private set; }

    /// <summary>The ordered classification described on the type. Derived, never stored.</summary>
    public BoqSnapshotLineKind Kind =>
        VariationId.HasValue ? BoqSnapshotLineKind.Difference
        : InvoiceLineId.HasValue ? BoqSnapshotLineKind.Purchase
        : BoqSnapshotLineKind.BudgetItem;

    private BoqSnapshotLine()
    {
        // EF materialization.
    }

    public static BoqSnapshotLine Create(
        Guid boqSnapshotId,
        string description,
        decimal budgetedAmount,
        decimal actualAmount,
        decimal differenceAmount,
        decimal? quantity = null,
        string? unit = null,
        Guid? projectTaskId = null,
        Guid? invoiceLineId = null,
        Guid? variationId = null) =>
        new()
        {
            BoqSnapshotId = boqSnapshotId,
            Description = description,
            BudgetedAmount = budgetedAmount,
            ActualAmount = actualAmount,
            DifferenceAmount = differenceAmount,
            Quantity = quantity,
            Unit = unit,
            ProjectTaskId = projectTaskId,
            InvoiceLineId = invoiceLineId,
            VariationId = variationId,
        };
}

/// <summary>
/// The three line families a BOQ snapshot contains. <b>Derived from which source id is set, never
/// persisted</b> — the backend has no such column and adding one would let the stored value drift from
/// the ids it is supposed to describe.
/// </summary>
internal enum BoqSnapshotLineKind
{
    BudgetItem = 1,
    Purchase = 2,
    Difference = 3,
}
