using SharedKernel;

namespace Identity.Domain;

internal static class PermissionErrors
{
    public static readonly Error PermissionOverrideNotFound =
        Error.NotFound("permissions.PermissionOverrideNotFound", "Permission override was not found.");

    public static Error InvalidPermission(string permission) =>
        Error.BadRequest("permissions.InvalidPermission", $"'{permission}' is not a recognized permission.");

    public static Error NotFoundForTarget(string permission) =>
        Error.NotFound("permissions.NotFoundForTarget", $"No override found for permission '{permission}'.");

    /// <summary>N1: a caller may only confer what they themselves effectively hold.</summary>
    public static Error CannotDelegate(IEnumerable<string> missingPermissions) =>
        Error.Forbidden(
            "permissions.CannotDelegate",
            $"You cannot confer permissions you do not hold: {string.Join(", ", missingPermissions)}.");

    /// <summary>N1: full-access roles are managed only by members of a full-access role.</summary>
    public static Error CannotDelegateFullAccessRole(string roleName) =>
        Error.Forbidden(
            "permissions.CannotDelegate",
            $"Only a member of a full-access role can assign, remove or edit the '{roleName}' role.");

    /// <summary>K6: nobody grants or denies overrides on their own account.</summary>
    public static readonly Error SelfModification =
        Error.Forbidden("permissions.SelfModification", "You cannot grant or deny permissions on your own account.");

    /// <summary>K6: the system must keep at least one active user who can manage permissions.</summary>
    public static Error LastHolder(string permission) =>
        Error.Conflict(
            "permissions.LastHolder",
            $"Denying '{permission}' would leave no active user holding it.");

    public static Error PermissionOverrideAlreadyExists(string permission) =>
        Error.Conflict(
            "permissions.PermissionOverrideAlreadyExists",
            $"An override for permission '{permission}' already exists.");
}
