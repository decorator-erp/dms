using BuildingBlocks;

namespace Identity.Contracts;

/// <summary>Collects <see cref="IModulePermissions"/> from every module — Identity must not hold a
/// hard-coded list of other modules' permissions.</summary>
public interface IPermissionCatalog
{
    IReadOnlyList<ModulePermissionSet> All();
}

public sealed record ModulePermissionSet(string Module, IReadOnlyList<string> Permissions);
