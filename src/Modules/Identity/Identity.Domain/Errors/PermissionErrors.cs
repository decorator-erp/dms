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

    public static Error PermissionOverrideAlreadyExists(string permission) =>
        Error.Conflict(
            "permissions.PermissionOverrideAlreadyExists",
            $"An override for permission '{permission}' already exists.");
}
