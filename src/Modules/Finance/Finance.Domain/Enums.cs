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

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/InvoiceStatus.cs</c>.</summary>
internal enum InvoiceStatus
{
    Open = 1,
    Paid = 2,
    Cancelled = 3,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/InvoiceType.cs</c>.</summary>
internal enum InvoiceType
{
    ClientInvoice = 1,
    VendorInvoice = 2,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/VariationStatus.cs</c>.</summary>
internal enum VariationStatus
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Rejected = 4,
    Cancelled = 5,
    Applied = 6,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/VariationKind.cs</c>.</summary>
internal enum VariationKind
{
    ClientDifference = 1,
    InternalChange = 2,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/BoqSnapshotType.cs</c>.</summary>
internal enum BoqSnapshotType
{
    StageClose = 1,
    ProjectFinal = 2,
    Manual = 3,
}
