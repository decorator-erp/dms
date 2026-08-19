using System.Reflection;
using System.Runtime.CompilerServices;
using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Rule 3: no module may reference another module's <c>.Domain</c>, <c>.Application</c>, or
/// <c>.Infrastructure</c>. Plus the plan's addition: no module grants <c>InternalsVisibleTo</c> across
/// a module boundary either — that would let another module reach in and touch <c>internal</c> types
/// directly, defeating the point of marking them internal in the first place.
///
/// Vacuous today — fewer than two modules exist. Becomes non-vacuous once both Identity and HR have at
/// least one of these three layers (G1-02 / G2-02 onward).
/// </summary>
public class Rule3ModuleIsolationTests
{
    private static IReadOnlyList<Assembly> LayeredModuleAssemblies =>
        [.. ModuleAssemblies.WithSuffix(".Domain")
            .Concat(ModuleAssemblies.WithSuffix(".Application"))
            .Concat(ModuleAssemblies.WithSuffix(".Infrastructure"))];

    [Fact]
    public void Modules_MustNotReferenceEachOthersDomainApplicationOrInfrastructure()
    {
        var layered = LayeredModuleAssemblies;

        // PC-03 liveness: eight modules x three layered assemblies. An empty (or single-module) set
        // makes the `otherModuleAssemblyNames.Length == 0` continue below fire for every iteration,
        // and the rule reports isolation it never tested.
        Assert.True(layered.Count >= 24,
            $"Discovered only {layered.Count} layered module assemblies, expected at least 24.");

        foreach (var assembly in layered)
        {
            var ownModule = ModuleAssemblies.ModuleOf(assembly.GetName().Name!);

            var otherModuleAssemblyNames = layered
                .Select(a => a.GetName().Name!)
                .Where(name => ModuleAssemblies.ModuleOf(name) != ownModule)
                .ToArray();

            if (otherModuleAssemblyNames.Length == 0)
                continue;

            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny(otherModuleAssemblyNames)
                .GetResult();

            ArchAssert.Passes(assembly, result);
        }
    }

    [Fact]
    public void Modules_MustNotGrantInternalsVisibleToAcrossAModuleBoundary()
    {
        // PC-03 liveness — same reason as the test above: no assemblies means no grants examined.
        Assert.True(LayeredModuleAssemblies.Count >= 24,
            $"Discovered only {LayeredModuleAssemblies.Count} layered module assemblies, expected at least 24.");

        foreach (var assembly in LayeredModuleAssemblies)
        {
            var ownModule = ModuleAssemblies.ModuleOf(assembly.GetName().Name!);

            var crossModuleGrants = assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
                .Select(a => new AssemblyName(a.AssemblyName).Name!)
                .Where(grantedTo => ModuleAssemblies.ModuleOf(grantedTo) != ownModule)
                .ToList();

            Assert.True(crossModuleGrants.Count == 0,
                $"{assembly.GetName().Name} grants InternalsVisibleTo across a module boundary: "
                + string.Join(", ", crossModuleGrants));
        }
    }
}
