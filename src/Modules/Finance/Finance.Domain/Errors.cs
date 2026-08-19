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
