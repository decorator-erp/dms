namespace BuildingBlocks;

/// <summary>
/// One module's registered permission strings. A list of strings, no ASP.NET involved — this is why it
/// lives in <c>BuildingBlocks</c> rather than <c>BuildingBlocks.Web</c>: <c>Identity.Contracts</c>'
/// <c>IPermissionCatalog</c> collects these from every module to seed roles and drive the role-admin
/// UI. Identity knows the strings, never their meaning.
/// </summary>
public interface IModulePermissions
{
    string Module { get; }
    IReadOnlyList<string> Permissions { get; }
}

/// <summary>Trivial default implementation — every module's registration is the same one-liner
/// (<c>new ModulePermissionsSet("Identity", [...])</c>), so it lives here once rather than being
/// redeclared privately per module.</summary>
public sealed record ModulePermissionsSet(string Module, IReadOnlyList<string> Permissions) : IModulePermissions;
