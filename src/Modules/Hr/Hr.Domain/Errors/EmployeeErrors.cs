using SharedKernel;

namespace Hr.Domain;

internal static class EmployeeErrors
{
    public static readonly Error NotFound = Error.NotFound("employees.NotFound", "Employee was not found.");

    public static readonly Error AlreadyInactive =
        Error.Conflict("employees.AlreadyInactive", "Employee is already inactive.");

    public static readonly Error AlreadyActive =
        Error.Conflict("employees.AlreadyActive", "Employee is already active.");
}
