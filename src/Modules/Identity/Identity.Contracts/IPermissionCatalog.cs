namespace Identity.Contracts;

public interface IPermissionCatalog
{
    IReadOnlyList<ModulePermissionSet> All();
}

public sealed record ModulePermissionSet(string Module, IReadOnlyList<string> Permissions);
