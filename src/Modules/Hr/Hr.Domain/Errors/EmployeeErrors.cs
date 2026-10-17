using SharedKernel;

namespace Hr.Domain.Errors;

internal static class EmployeeErrors
{
    public static readonly Error NotFound = Error.NotFound("employees.NotFound", "Employee was not found.");

    public static readonly Error AlreadyInactive =
        Error.Conflict("employees.AlreadyInactive", "Employee is already inactive.");

    public static readonly Error AlreadyActive =
        Error.Conflict("employees.AlreadyActive", "Employee is already active.");

    public static readonly Error FailedToActivate =
        Error.BadRequest("employees.FailedToActivate", "Failed to activate employee.");

    public static readonly Error InvalidUserId =
        Error.BadRequest("employees.InvalidUserId", "Invalid user ID for employee.");
}
