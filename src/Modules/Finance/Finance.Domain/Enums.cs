namespace Finance.Domain;

/// <summary>
/// Ported verbatim from <c>../backend/Domain/Enums/LedgerDirection.cs</c>.
///
/// <para>These enums are deliberately duplicated between <c>Finance.Domain</c> (internal, here) and
/// <c>Finance.Contracts</c> (public), with a mapping shim at the persistence/projection boundary —
/// the same arrangement Projects uses (<c>ProjectsEnumMappingShim</c>). Rule 4 requires that only
/// <c>*.Contracts</c> types are public, and the Domain must not depend on Contracts, so one enum
/// cannot serve both. The numeric values are identical and a unit test pins them together, because
/// they are persisted as <c>int</c> and a silent divergence would remap every stored row.</para>
/// </summary>
internal enum LedgerDirection
{
    In = 1,
    Out = 2,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/LedgerEntryType.cs</c>.</summary>
internal enum LedgerEntryType
{
    Commitment = 1,
    ActualCost = 2,
    ClientPayment = 3,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/PaymentMethod.cs</c>.</summary>
internal enum PaymentMethod
{
    Cash = 1,
    BankTransfer = 2,
    Cheque = 3,
    Card = 4,
    Other = 5,
}
