using SharedKernel;

namespace Identity.Domain;

internal static class RoleErrors
{
    public static readonly Error Unauthorized =
        Error.Unauthorized("roles.Unauthorized", "You are not authorized to manage roles.");

    public static Error DuplicatedRole(string roleName) =>
        Error.BadRequest("roles.DuplicatedRole", $"Role '{roleName}' already exists.");

    public static readonly Error NotFound = Error.NotFound("roles.NotFound", "Role was not found.");

    /// <summary>N3: full-access roles always hold the whole catalog; their claims are not editable.</summary>
    public static Error FullAccessLocked(string roleName) =>
        Error.Conflict(
            "roles.FullAccessLocked",
            $"The '{roleName}' role always holds every permission; its permissions cannot be edited.");

    /// <summary>N4: the seeded roles are referenced by name (provisioning, seeders); they keep it.</summary>
    public static Error SystemRole(string roleName) =>
        Error.Conflict("roles.SystemRole", $"'{roleName}' is a system role and cannot be renamed or deleted.");

    /// <summary>N4: deleting a role still held would silently strip it from every member.</summary>
    public static Error RoleInUse(string roleId, int memberCount) =>
        Error.Conflict(
            "roles.RoleInUse",
            $"Role '{roleId}' is still assigned to {memberCount} user(s); remove them from it first.");

    public static readonly Error UserNotInRole =
        Error.BadRequest("roles.UserNotInRole", "User is not in the specified role.");
}
