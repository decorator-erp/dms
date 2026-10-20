using SharedKernel;

namespace Hr.Domain.Errors;

internal static class EmployeeErrors
{
    public static readonly Error NotFound = Error.NotFound("employees.NotFound", "Employee was not found.");

    public static readonly Error AlreadyInactive =
        Error.Conflict("employees.AlreadyInactive", "Employee is already inactive.");

    public static readonly Error AlreadyActive =
        Error.Conflict("employees.AlreadyActive", "Employee is already active.");

    /// <summary>Phase 2 (A3, N7): the operation needs an active employee. One answer everywhere: update,
    /// department assignment, move and headship all say this rather than 404.</summary>
    public static readonly Error Inactive = Error.Conflict("employees.Inactive", "Employee is inactive.");

    /// <summary>K1: an employee is active in at most one department; moving them is a move, not an assign.</summary>
    /// <param name="currentDepartmentId">Null when the conflict was only detected by the database (a
    /// concurrent assignment won the race), where the transaction is aborted and can't be asked which.</param>
    public static Error AlreadyInAnotherDepartment(Guid? currentDepartmentId) =>
        Error.Conflict(
            "employees.AlreadyInAnotherDepartment",
            currentDepartmentId is { } id
                ? $"Employee is already in department '{id}'. Move them instead."
                : "Employee is already in another department. Move them instead.");

    public static readonly Error InvalidUserId =
        Error.BadRequest("employees.InvalidUserId", "Invalid user ID for employee.");
}
