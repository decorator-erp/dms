using SharedKernel;

namespace Finance.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/LedgerAllocation.cs</c>. Settles part or all of a payment
/// entry against one invoice. Many-to-many and partial by design: one payment can clear several
/// invoices and one invoice can be cleared by several payments.
///
/// <para><b>The bounding invariant does not live here, and cannot.</b> "Allocations must never exceed
/// the entry's amount" is a fact about the <i>set</i> of allocations on an entry, which a single row
/// cannot see. <see cref="Create"/> enforces only the per-row rule (positive amount);
/// <see cref="WouldExceedUnallocated"/> expresses the set-level rule so the handler that does have the
/// sum applies the same arithmetic rather than re-deriving it.</para>
/// </summary>
internal sealed class LedgerAllocation : Entity, IAuditable
{
    public Guid LedgerEntryId { get; private set; }
    public Guid InvoiceId { get; private set; }
    public decimal Amount { get; private set; }

    private LedgerAllocation()
    {
        // EF materialization.
    }

    public static Result<LedgerAllocation> Create(Guid ledgerEntryId, Guid invoiceId, decimal amount)
    {
        if (amount <= 0)
        {
            return LedgerAllocationErrors.AmountMustBePositive;
        }

        return new LedgerAllocation
        {
            LedgerEntryId = ledgerEntryId,
            InvoiceId = invoiceId,
            Amount = amount,
        };
    }

    /// <summary>
    /// The set-level bound, stated once. <paramref name="alreadyAllocated"/> is the sum of existing
    /// allocations on the entry and <paramref name="entryAmount"/> is the entry's own amount.
    ///
    /// <para>Exact equality is allowed — allocating a payment down to zero remaining is the normal
    /// case, not an overflow.</para>
    /// </summary>
    public static bool WouldExceedUnallocated(decimal entryAmount, decimal alreadyAllocated, decimal requested) =>
        alreadyAllocated + requested > entryAmount;
}
