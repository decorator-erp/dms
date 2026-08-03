using SharedKernel;

namespace Identity.Domain;

internal static class RoleErrors
{
    public static readonly Error Unauthorized =
        Error.Unauthorized("roles.Unauthorized", "You are not authorized to manage roles.");

    public static Error DuplicatedRole(string roleName) =>
        Error.BadRequest("roles.DuplicatedRole", $"Role '{roleName}' already exists.");

    public static readonly Error NotFound = Error.NotFound("roles.NotFound", "Role was not found.");

    public static readonly Error UserNotInRole =
        Error.BadRequest("roles.UserNotInRole", "User is not in the specified role.");
}
