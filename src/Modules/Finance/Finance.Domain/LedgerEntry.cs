using SharedKernel;

namespace Finance.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/LedgerEntry.cs</c>. One immutable financial movement:
/// a commitment, an actual cost, or a client payment. Entries are never edited — a mistake is
/// corrected by posting a <i>reversal</i>, which is a second entry pointing at the first.
///
/// <para><b>No navigation properties at all</b>, matching the backend, which is convenient here: every
/// id on this entity except <see cref="BudgetLineId"/> and <see cref="VariationId"/> points outside
/// Finance.</para>
///
/// <para><b>Two invariants, and both are skipped for reversals.</b> A <c>ClientPayment</c> must be
/// <see cref="LedgerDirection.In"/> and must name a client; everything else must be
/// <see cref="LedgerDirection.Out"/>. A reversal is exempt from both <i>by construction</i>, because a
/// reversal's whole purpose is to carry the opposite direction of the entry it undoes — enforcing the
/// type/direction rule on it would make reversing a client payment impossible.</para>
/// </summary>
internal sealed class LedgerEntry : Entity, IAuditable
{
    /// <summary>Bare Guid → <c>projects.projects</c>.</summary>
    public Guid ProjectId { get; private set; }

    public Guid? ProjectStageId { get; private set; }
    public Guid? ProjectTaskId { get; private set; }
    public Guid? BudgetLineId { get; private set; }

    public LedgerDirection Direction { get; private set; }
    public LedgerEntryType Type { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentMethod? Method { get; private set; }

    /// <summary>Bare Guid → <c>crm.clients</c>.</summary>
    public Guid? ClientId { get; private set; }

    /// <summary>Free text. A vendor has no account in this system, which is why an invoice-due
    /// reminder can never reach one — see F1-08's recipient rule.</summary>
    public string? VendorName { get; private set; }

    public Guid? VariationId { get; private set; }

    /// <summary>Set on a reversal, naming the entry being undone. Its presence is what exempts this
    /// entry from the direction and party invariants, and what makes an entry a reversal at all.</summary>
    public Guid? ReversalOfId { get; private set; }

    public DateOnly OccurredOn { get; private set; }
    public string? Reference { get; private set; }
    public string? Note { get; private set; }

    /// <summary>Bare Guid → <c>hr.employees</c>.</summary>
    public Guid RecordedById { get; private set; }

    public bool IsReversal => ReversalOfId.HasValue;

    private LedgerEntry()
    {
        // EF materialization.
    }

    public static Result<LedgerEntry> Create(
        Guid projectId,
        decimal amount,
        LedgerDirection direction,
        LedgerEntryType type,
        DateOnly occurredOn,
        Guid recordedById,
        Guid? projectStageId = null,
        Guid? projectTaskId = null,
        Guid? budgetLineId = null,
        PaymentMethod? method = null,
        Guid? clientId = null,
        string? vendorName = null,
        Guid? variationId = null,
        Guid? reversalOfId = null,
        string? reference = null,
        string? note = null)
    {
        // Always positive. A reversal carries the same magnitude and flips Direction instead of
        // negating Amount, so no consumer ever has to decide whether a number is already signed.
        if (amount <= 0)
            return LedgerEntryErrors.AmountMustBePositive;

        var entry = new LedgerEntry
        {
            ProjectId = projectId,
            Amount = amount,
            Direction = direction,
            Type = type,
            OccurredOn = occurredOn,
            RecordedById = recordedById,
            ProjectStageId = projectStageId,
            ProjectTaskId = projectTaskId,
            BudgetLineId = budgetLineId,
            Method = method,
            ClientId = clientId,
            VendorName = vendorName,
            VariationId = variationId,
            ReversalOfId = reversalOfId,
            Reference = reference,
            Note = note,
        };

        if (ValidateDirection(entry) is { IsFailure: true } direction_)
            return direction_.Error;

        if (ValidateParty(entry) is { IsFailure: true } party)
            return party.Error;

        return entry;
    }

    /// <summary>Builds the mirror entry that reverses <paramref name="original"/>: same magnitude, same
    /// scope, flipped direction, <see cref="ReversalOfId"/> set. Kept here rather than in a handler so
    /// "a reversal is exact" is a property of the type, not of one call site.</summary>
    public static Result<LedgerEntry> CreateReversal(LedgerEntry original, Guid recordedById, string? note = null) =>
        Create(
            original.ProjectId,
            original.Amount,
            original.Direction == LedgerDirection.In ? LedgerDirection.Out : LedgerDirection.In,
            original.Type,
            original.OccurredOn,
            recordedById,
            original.ProjectStageId,
            original.ProjectTaskId,
            original.BudgetLineId,
            original.Method,
            original.ClientId,
            original.VendorName,
            original.VariationId,
            reversalOfId: original.Id,
            reference: original.Reference,
            note: note);

    private static Result ValidateDirection(LedgerEntry entry)
    {
        if (entry.ReversalOfId.HasValue)
            return Result.Success();

        var valid = entry.Type switch
        {
            LedgerEntryType.ClientPayment => entry.Direction == LedgerDirection.In,
            _ => entry.Direction == LedgerDirection.Out,
        };

        return valid ? Result.Success() : LedgerEntryErrors.InvalidDirection;
    }

    private static Result ValidateParty(LedgerEntry entry)
    {
        if (entry.ReversalOfId.HasValue)
            return Result.Success();

        var valid = entry.Type switch
        {
            LedgerEntryType.ClientPayment => entry.ClientId.HasValue,
            _ => true,
        };

        return valid ? Result.Success() : LedgerEntryErrors.InvalidPartyForType;
    }
}
