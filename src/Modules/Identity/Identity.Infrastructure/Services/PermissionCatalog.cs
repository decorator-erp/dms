using BuildingBlocks;
using Identity.Contracts;

namespace Identity.Infrastructure.Services;

/// <summary>
/// Collects every module's registered <see cref="IModulePermissions"/> — each module registers its own
/// as a singleton (see <c>IdentityModule.Register</c>) — into the union <see cref="IPermissionCatalog"/>
/// exposes. Identity never hard-codes another module's permission strings; this is the mechanism that
/// makes that true instead of just asserted.
/// </summary>
internal sealed class PermissionCatalog(IEnumerable<IModulePermissions> modulePermissions) : IPermissionCatalog
{
    public IReadOnlyList<ModulePermissionSet> All() =>
        [.. modulePermissions.Select(m => new ModulePermissionSet(m.Module, m.Permissions))];
}
