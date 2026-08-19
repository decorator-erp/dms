namespace Finance.Application;

/// <summary>
/// <c>Finance.Application</c> legitimately references both <c>Finance.Contracts</c> and
/// <c>Finance.Domain</c> — those two cannot reference each other (the reference matrix forbids
/// Domain → Contracts, and Contracts → Domain would make internals public), so the eight enum pairs are
/// duplicated by construction and every handler here converts between them.
///
/// <para>A numeric cast is correct because both copies are ported from the same backend enum, in the
/// same order, in the same commit — the same deliberate, disclosed trade-off <c>SiteEnumMapping</c>,
/// <c>CrmEnumMapping</c> and <c>ProjectsEnumMapping</c> already make.
/// <c>Finance.UnitTests.EnumParityTests</c> enumerates every value of every pair and asserts names and
/// underlying values match exactly, which is the safety an exhaustive hand-written switch would give
/// for a fraction of the code.</para>
///
/// <para><b>Finance has a second reason to trust the numbers, unique to this module:</b> the two
/// <c>variations</c> check constraints compare <c>"Kind"</c> against the literals <c>1</c> and
/// <c>2</c>. A divergence between the two copies would not merely mis-project a response, it would
/// unarm a database constraint. See <c>VariationConfiguration</c>.</para>
///
/// <para>No pair is renamed across the boundary — none of Finance's enum names collides with another
/// module's Contracts type.</para>
/// </summary>
internal static class FinanceEnumMapping
{
    public static Domain.LedgerDirection ToDomain(Contracts.LedgerDirection value) =>
        (Domain.LedgerDirection)value;

    public static Contracts.LedgerDirection ToContracts(Domain.LedgerDirection value) =>
        (Contracts.LedgerDirection)value;

    public static Domain.LedgerEntryType ToDomain(Contracts.LedgerEntryType value) =>
        (Domain.LedgerEntryType)value;

    public static Contracts.LedgerEntryType ToContracts(Domain.LedgerEntryType value) =>
        (Contracts.LedgerEntryType)value;

    public static Domain.PaymentMethod? ToDomain(Contracts.PaymentMethod? value) =>
        (Domain.PaymentMethod?)value;

    public static Contracts.PaymentMethod? ToContracts(Domain.PaymentMethod? value) =>
        (Contracts.PaymentMethod?)value;

    public static Domain.InvoiceStatus ToDomain(Contracts.InvoiceStatus value) =>
        (Domain.InvoiceStatus)value;

    public static Contracts.InvoiceStatus ToContracts(Domain.InvoiceStatus value) =>
        (Contracts.InvoiceStatus)value;

    public static Domain.InvoiceType ToDomain(Contracts.InvoiceType value) => (Domain.InvoiceType)value;

    public static Contracts.InvoiceType ToContracts(Domain.InvoiceType value) =>
        (Contracts.InvoiceType)value;

    public static Domain.VariationStatus ToDomain(Contracts.VariationStatus value) =>
        (Domain.VariationStatus)value;

    public static Contracts.VariationStatus ToContracts(Domain.VariationStatus value) =>
        (Contracts.VariationStatus)value;

    public static Domain.VariationKind ToDomain(Contracts.VariationKind value) =>
        (Domain.VariationKind)value;

    public static Contracts.VariationKind ToContracts(Domain.VariationKind value) =>
        (Contracts.VariationKind)value;

    public static Domain.BoqSnapshotType ToDomain(Contracts.BoqSnapshotType value) =>
        (Domain.BoqSnapshotType)value;

    public static Contracts.BoqSnapshotType ToContracts(Domain.BoqSnapshotType value) =>
        (Contracts.BoqSnapshotType)value;
}
