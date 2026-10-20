using Mono.Cecil;

namespace Architecture.Tests;

/// <summary>
/// The <c>src</c> assemblies as IL, read with Mono.Cecil (NetArchTest's own dependency), for the rules
/// that need to see inside method bodies, which reflection cannot (Rules 13 and 16). Read from this test
/// project's output directory, the same place <see cref="ModuleAssemblies"/> loads from.
/// </summary>
internal static class IlAssemblies
{
    /// <summary>The non-module <c>src</c> assemblies that can construct or use module types.</summary>
    private static readonly string[] HostAssemblies = ["Api", "BuildingBlocks", "BuildingBlocks.Web", "SharedKernel"];

    private static readonly Lazy<IReadOnlyList<ModuleDefinition>> Loaded = new(LoadAll);

    /// <summary>Every module assembly plus the host ones, never a test assembly.</summary>
    public static IReadOnlyList<ModuleDefinition> AllSource => Loaded.Value;

    public static IEnumerable<ModuleDefinition> WithSuffix(string suffix) =>
        AllSource.Where(m => m.Assembly.Name.Name.EndsWith(suffix, StringComparison.Ordinal));

    /// <summary>The namespace a type reference lives in, walking out of nested types (whose own
    /// <c>Namespace</c> is empty).</summary>
    public static string NamespaceOf(TypeReference type)
    {
        while (type.DeclaringType is not null)
        {
            type = type.DeclaringType;
        }

        return type.Namespace;
    }

    private static IReadOnlyList<ModuleDefinition> LoadAll()
    {
        string baseDir = AppContext.BaseDirectory;
        return [.. ModuleAssemblies.All
            .Select(a => a.GetName().Name!)
            .Concat(HostAssemblies)
            .Select(name => ModuleDefinition.ReadModule(Path.Combine(baseDir, name + ".dll")))];
    }
}
