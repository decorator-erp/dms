using BuildingBlocks.Web;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Not one of PLAN.md's nine numbered rules, but a corollary G0-06 needs: rule 8 stops
/// <c>*.Application</c>/<c>*.Domain</c> from referencing <c>BuildingBlocks.Web</c>, but nothing stops
/// <c>Api</c> itself (which may reference everything) from implementing <see cref="IModule"/> directly.
/// G0-06's verify text — "every IModule implementation lives in a *.Infrastructure assembly" — needs
/// its own check for that reason.
///
/// Vacuous today — no <see cref="IModule"/> implementation exists yet. Becomes non-vacuous at G1-03
/// (<c>IdentityModule</c>).
/// </summary>
public class ModuleContractTests
{
    [Fact]
    public void EveryIModuleImplementation_LivesInAnInfrastructureAssembly()
    {
        var infrastructureAssemblyNames = ModuleAssemblies.WithSuffix(".Infrastructure")
            .Select(a => a.GetName().Name)
            .ToHashSet();

        // PC-03 liveness: the scan must actually find IModule implementations. If reflection returned
        // none, `offenders` is empty and "every IModule lives in an Infrastructure assembly" passes
        // having located no IModule at all. One per module, nine modules.
        var implementations = ModuleAssemblies.All
            .SelectMany(a => ModuleAssemblies.SafeGetTypes(a)
                .Where(t => typeof(IModule).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false }))
            .ToList();

        Assert.True(implementations.Count >= 9,
            $"Found only {implementations.Count} IModule implementations, expected at least 9.");

        var offenders = ModuleAssemblies.All
            .SelectMany(a => ModuleAssemblies.SafeGetTypes(a)
                .Where(t => typeof(IModule).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
                .Select(t => (Assembly: a, Type: t)))
            .Where(x => !infrastructureAssemblyNames.Contains(x.Assembly.GetName().Name))
            .Select(x => $"{x.Type.FullName} in {x.Assembly.GetName().Name}")
            .ToList();

        Assert.True(offenders.Count == 0, string.Join(", ", offenders));
    }
}
