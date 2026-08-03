using SharedKernel;

namespace Hr.Domain;

internal static class DepartmentErrors
{
    public static Error DuplicatedName(string name) =>
        Error.BadRequest("departments.DuplicatedName", $"Department with name '{name}' already exists.");

    public static readonly Error NotFound = Error.NotFound("departments.NotFound", "Department was not found.");

    public static readonly Error AlreadyInDepartment =
        Error.BadRequest("departments.AlreadyInDepartment", "Employee is already in this department.");

    public static readonly Error UserNotInDepartment =
        Error.BadRequest("departments.UserNotInDepartment", "Employee is not in the specified department.");

    public static readonly Error NoEmployeesToMove =
        Error.BadRequest("departments.NoEmployeesToMove", "No employees to move in the specified department.");

    /// <summary>D7 bug fixed on the way in: the backend's version emits
    /// <c>nameof(NoEmployeesToMove)</c> as this error's code — copy-paste from the entry above it — so
    /// callers checking <c>Error.Code == "departments.DepartmentHeadNotAssign"</c> would never match.
    /// Gets its own code here.</summary>
    public static readonly Error DepartmentHeadNotAssign =
        Error.BadRequest("departments.DepartmentHeadNotAssign", "This department has no assigned head.");

    public static readonly Error AlreadyHaveHead =
        Error.BadRequest("departments.AlreadyHaveHead", "This department already has a head assigned.");
}
