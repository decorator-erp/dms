namespace Finance.Contracts;

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/LedgerDirection.cs</c>.</summary>
public enum LedgerDirection
{
    In = 1,
    Out = 2,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/LedgerEntryType.cs</c>.</summary>
public enum LedgerEntryType
{
    Commitment = 1,
    ActualCost = 2,
    ClientPayment = 3,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/PaymentMethod.cs</c>.</summary>
public enum PaymentMethod
{
    Cash = 1,
    BankTransfer = 2,
    Cheque = 3,
    Card = 4,
    Other = 5,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/InvoiceStatus.cs</c>.</summary>
public enum InvoiceStatus
{
    Open = 1,
    Paid = 2,
    Cancelled = 3,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/InvoiceType.cs</c>.</summary>
public enum InvoiceType
{
    ClientInvoice = 1,
    VendorInvoice = 2,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/VariationStatus.cs</c>.</summary>
public enum VariationStatus
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Rejected = 4,
    Cancelled = 5,
    Applied = 6,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/VariationKind.cs</c>.</summary>
public enum VariationKind
{
    ClientDifference = 1,
    InternalChange = 2,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/BoqSnapshotType.cs</c>.</summary>
public enum BoqSnapshotType
{
    StageClose = 1,
    ProjectFinal = 2,
    Manual = 3,
}
