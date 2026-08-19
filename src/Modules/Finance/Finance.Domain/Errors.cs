using SharedKernel;

namespace Finance.Domain;

/// <summary>Ported from <c>../backend/Domain/Errors/CostCategoryErrors.cs</c>.</summary>
internal static class CostCategoryErrors
{
    private const string Code = "cost_categories";

    public static readonly Error NotFound = Error.NotFound($"{Code}.NotFound", "Cost category was not found.");

    public static readonly Error DuplicatedCode = Error.Conflict(
        $"{Code}.DuplicatedCode", "A cost category with this code already exists.");

    public static readonly Error CodeRequired = Error.BadRequest($"{Code}.CodeRequired", "Cost category code is required.");
    public static readonly Error NameRequired = Error.BadRequest($"{Code}.NameRequired", "Cost category name is required.");
}

/// <summary>Ported from <c>../backend/Domain/Errors/BudgetLineErrors.cs</c>.</summary>
internal static class BudgetLineErrors
{
    private const string Code = "budget_lines";

    public static readonly Error NotFound = Error.NotFound($"{Code}.NotFound", "Budget line was not found.");
    public static readonly Error NameRequired = Error.BadRequest($"{Code}.NameRequired", "Budget line name is required.");

    public static readonly Error NegativeAllocation = Error.BadRequest(
        $"{Code}.NegativeAllocation", "Allocated amount cannot be negative.");

    public static readonly Error TaskStageMismatch = Error.BadRequest(
        $"{Code}.TaskStageMismatch",
        "Task-scoped budget line requires a matching stage scope and the task must belong to that stage and project.");

    public static readonly Error CostCategoryNotFound = Error.NotFound(
        $"{Code}.CostCategoryNotFound", "The cost category was not found.");

    /// <summary>F1-06 addition. The backend validates the task's scope but never the project on its
    /// own, so a budget line naming an unknown project reaches the database and fails as a foreign-key
    /// violation — a 500 where a 404 belongs. Same defect and same fix as Sites' B1.</summary>
    public static readonly Error ProjectNotFound = Error.NotFound(
        $"{Code}.ProjectNotFound", "The project for this budget line was not found.");
}

/// <summary>Ported from <c>../backend/Domain/Errors/LedgerEntryErrors.cs</c>.</summary>
internal static class LedgerEntryErrors
{
    private const string Code = "ledger_entries";

    public static readonly Error NotFound = Error.NotFound($"{Code}.NotFound", "Ledger entry was not found.");

    public static readonly Error AmountMustBePositive = Error.BadRequest(
        $"{Code}.AmountMustBePositive", "Ledger entry amount must be greater than zero.");

    public static readonly Error InvalidDirection = Error.BadRequest(
        $"{Code}.InvalidDirection", "The direction does not match the ledger entry type.");

    public static readonly Error InvalidPartyForType = Error.BadRequest(
        $"{Code}.InvalidPartyForType", "The ledger entry type requires a matching party (client / vendor / variation).");

    public static readonly Error DuplicatePosting = Error.Conflict(
        $"{Code}.DuplicatePosting", "A ledger entry for this source has already been posted.");

    public static readonly Error InsufficientBudget = Error.BadRequest(
        $"{Code}.InsufficientBudget",
        "The commitment exceeds the budget line's available amount; raise it via a variation first.");

    public static readonly Error AlreadyReversed = Error.Conflict(
        $"{Code}.AlreadyReversed", "This ledger entry has already been reversed.");

    public static readonly Error NotReversible = Error.BadRequest(
        $"{Code}.NotReversible", "Only cleared ledger entries can be reversed.");

    public static readonly Error NotAPayment = Error.BadRequest(
        $"{Code}.NotAPayment", "Only cleared client/vendor payment entries can be allocated to invoices.");
}

/// <summary>Ported from <c>../backend/Domain/Errors/LedgerAllocationErrors.cs</c>.</summary>
internal static class LedgerAllocationErrors
{
    private const string Code = "ledger_allocations";

    public static readonly Error AmountMustBePositive = Error.BadRequest(
        $"{Code}.AmountMustBePositive", "Allocation amount must be greater than zero.");

    public static readonly Error ExceedsUnallocated = Error.BadRequest(
        $"{Code}.ExceedsUnallocated", "Allocations exceed the unallocated amount of the ledger entry.");
}

/// <summary>Ported from <c>../backend/Domain/Errors/InvoiceErrors.cs</c>.</summary>
internal static class InvoiceErrors
{
    private const string Code = "invoices";

    public static readonly Error NotFound = Error.NotFound($"{Code}.NotFound", "Invoice was not found.");

    public static readonly Error DuplicatedNumber = Error.Conflict(
        $"{Code}.DuplicatedNumber", "An invoice with this number already exists.");

    /// <summary>F1-03 addition. The backend's <c>Invoice.Create</c> never checks the number, relying on
    /// a FluentValidation rule at the edge — so a handler constructing an invoice directly could
    /// persist one with an empty number, which is also the natural key humans and the duplicate check
    /// use.</summary>
    public static readonly Error NumberRequired = Error.BadRequest(
        $"{Code}.NumberRequired", "Invoice number is required.");

    public static readonly Error ClientRequired = Error.BadRequest(
        $"{Code}.ClientRequired", "A client invoice requires a client.");

    public static readonly Error VendorRequired = Error.BadRequest(
        $"{Code}.VendorRequired", "A vendor invoice requires a vendor name.");

    public static readonly Error NotPayable = Error.BadRequest(
        $"{Code}.NotPayable", "Payments can only be allocated to open invoices.");

    public static readonly Error OverAllocation = Error.BadRequest(
        $"{Code}.OverAllocation", "The allocation exceeds the invoice's outstanding balance.");

    public static readonly Error InvalidStatusTransition = Error.BadRequest(
        $"{Code}.InvalidStatusTransition", "The invoice status does not allow this operation.");

    /// <summary>F1-07 addition, same shape as the budget-line and BOQ cases: the backend leaves an
    /// unknown project to the foreign key, which surfaces as a 500 rather than a 404.</summary>
    public static readonly Error ProjectNotFound = Error.NotFound(
        $"{Code}.ProjectNotFound", "The project for this invoice was not found.");
}

/// <summary>Ported from <c>../backend/Domain/Errors/InvoiceLineErrors.cs</c>. The backend's
/// <c>InvalidNetAmount</c>/<c>InvalidLineTotal</c> are not ported: they describe a tax-bearing shape
/// (net + tax = total) this module does not model, and nothing raises them.</summary>
internal static class InvoiceLineErrors
{
    private const string Code = "invoice_lines";

    public static readonly Error DescriptionRequired = Error.BadRequest(
        $"{Code}.DescriptionRequired", "Invoice line description is required.");

    public static readonly Error InvalidQuantity = Error.BadRequest(
        $"{Code}.InvalidQuantity", "Invoice line quantity must be greater than zero.");

    /// <summary>F1-03 addition — the backend accepts a negative unit price, which produces a negative
    /// line total and silently reduces an invoice.</summary>
    public static readonly Error InvalidUnitPrice = Error.BadRequest(
        $"{Code}.InvalidUnitPrice", "Invoice line unit price cannot be negative.");
}

/// <summary>Ported from <c>../backend/Domain/Errors/VariationErrors.cs</c>.</summary>
internal static class VariationErrors
{
    private const string Code = "variations";

    public static readonly Error NotFound = Error.NotFound($"{Code}.NotFound", "Variation was not found.");

    public static readonly Error ReasonRequired = Error.BadRequest(
        $"{Code}.ReasonRequired", "Variation reason is required.");

    public static readonly Error MissingClientDifferenceFields = Error.BadRequest(
        $"{Code}.MissingClientDifferenceFields",
        "A client-difference variation requires a project task, a budgeted amount, and a chosen amount "
        + "greater than the budgeted amount.");

    public static readonly Error MissingBudgetLine = Error.BadRequest(
        $"{Code}.MissingBudgetLine", "An internal-change variation requires a budget line.");

    public static readonly Error AmountMustBePositive = Error.BadRequest(
        $"{Code}.AmountMustBePositive", "An internal-change variation requires a positive amount.");

    public static readonly Error SignatureFieldsNotAllowed = Error.BadRequest(
        $"{Code}.SignatureFieldsNotAllowed",
        "Client signature fields are only valid on client-difference variations.");

    public static readonly Error SignatureRequired = Error.BadRequest(
        $"{Code}.SignatureRequired", "Approving a client-difference variation requires the signing client.");

    public static readonly Error InvalidStatusTransition = Error.BadRequest(
        $"{Code}.InvalidStatusTransition", "The variation status does not allow this operation.");

    public static readonly Error NotApproved = Error.BadRequest(
        $"{Code}.NotApproved", "Only approved variations can be applied.");
}

/// <summary>F1-07. The financial dashboard is Finance-owned but project-scoped, and its first act is to
/// confirm the project exists. The backend returns <c>ProjectErrors.NotFound</c> — another module's
/// error, which this module cannot reference and should not: the caller asked Finance for a dashboard,
/// and the code it gets back should say which surface refused.</summary>
internal static class FinanceDashboardErrors
{
    public static readonly Error ProjectNotFound = Error.NotFound(
        "finance_dashboard.ProjectNotFound", "The project for this financial dashboard was not found.");
}

/// <summary>F1-07b. The project/stage/task read slice is scoped by ids Finance does not own, so "the
/// scope you asked about does not exist" is a distinct answer from "that scope has no financial
/// activity" — a caller can act on the difference, and collapsing the two into an empty list hides a
/// bad id behind a plausible-looking empty panel.</summary>
internal static class FinancialScopeErrors
{
    public static readonly Error StageNotFound = Error.NotFound(
        "financial.StageNotFound", "The project stage was not found.");
}

/// <summary>Ported from <c>../backend/Domain/Errors/BoqSnapshotErrors.cs</c>.</summary>
internal static class BoqSnapshotErrors
{
    private const string Code = "boq_snapshots";

    public static readonly Error NotFound = Error.NotFound($"{Code}.NotFound", "BOQ snapshot was not found.");

    public static readonly Error ProjectNotFound = Error.NotFound(
        $"{Code}.ProjectNotFound", "The project for this BOQ snapshot was not found.");
}
