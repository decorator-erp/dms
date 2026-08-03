using System.Reflection;

namespace BuildingBlocks.Web;

/// <summary>
/// Finds every <see cref="IModule"/> implementation in the given module assemblies. Implementations
/// are internal by convention (arch rule 4 exempts nothing here — <c>IModule</c> is the contract; the
/// class implementing it is module-internal), hence <c>nonPublic: true</c>.
/// </summary>
public static class ModuleDiscovery
{
    public static IReadOnlyList<IModule> DiscoverModules(params Assembly[] moduleAssemblies) =>
        [.. moduleAssemblies
            .SelectMany(a => a.DefinedTypes)
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IModule).IsAssignableFrom(t))
            .Select(t => (IModule)Activator.CreateInstance(t, nonPublic: true)!)];
}
