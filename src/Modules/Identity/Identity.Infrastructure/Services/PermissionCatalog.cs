using BuildingBlocks;
using Identity.Contracts;

namespace Identity.Infrastructure.Services;

internal sealed class PermissionCatalog(IEnumerable<IModulePermissions> modulePermissions) : IPermissionCatalog
{
    public IReadOnlyList<ModulePermissionSet> All() =>
        [.. modulePermissions.Select(m => new ModulePermissionSet(m.Module, m.Permissions))];
}
