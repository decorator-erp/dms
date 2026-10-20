using System.Reflection;

namespace Architecture.Tests;

/// <summary>
/// Discovers module assemblies by naming convention (<c>*.Domain</c>, <c>*.Application</c>,
/// <c>*.Infrastructure</c>, <c>*.Contracts</c>) from this test project's own output directory, rather
/// than from <see cref="AppDomain"/> — project references are guaranteed to have their DLLs copied
/// here at build time, whereas relying on assemblies already loaded into the process depends on
/// something having touched them first.
///
/// <b>Only what this project references.</b> All nine modules' four projects are referenced by
/// <c>Architecture.Tests.csproj</c> today; a module project that isn't would be invisible here, and every
/// rule built on this would pass over it. <see cref="MetaSelfEnforcementTests"/> fails when that happens.
/// </summary>
internal static class ModuleAssemblies
{
    private static readonly string[] Suffixes = [".Domain", ".Application", ".Infrastructure", ".Contracts"];

    private static readonly Lazy<IReadOnlyList<Assembly>> Loaded = new(LoadAll);

    /// <summary>Every module assembly found, across all four suffixes.</summary>
    public static IReadOnlyList<Assembly> All => Loaded.Value;

    public static IReadOnlyList<Assembly> WithSuffix(string suffix) =>
        [.. All.Where(a => a.GetName().Name?.EndsWith(suffix, StringComparison.Ordinal) == true)];

    /// <summary>The module prefix of an assembly name — "Identity" from "Identity.Domain".</summary>
    public static string ModuleOf(string assemblyName)
    {
        int dot = assemblyName.IndexOf('.');
        return dot < 0 ? assemblyName : assemblyName[..dot];
    }

    public static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

    private static IReadOnlyList<Assembly> LoadAll()
    {
        string baseDir = AppContext.BaseDirectory;

        return [.. Directory.GetFiles(baseDir, "*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null && Suffixes.Any(s => name.EndsWith(s, StringComparison.Ordinal)))
            .Select(name => Assembly.LoadFrom(Path.Combine(baseDir, name + ".dll")))];
    }
}
