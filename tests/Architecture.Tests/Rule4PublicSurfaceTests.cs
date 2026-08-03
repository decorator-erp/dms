using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Rule 4: only <c>*.Contracts</c> may contain public types — scoped to <c>*.Domain</c>,
/// <c>*.Application</c>, <c>*.Infrastructure</c>, excluding types deriving from <see cref="Migration"/>
/// or <see cref="ModelSnapshot"/> (generated on every <c>dotnet ef migrations add</c>, never hand-authored,
/// so this rule was never meant to constrain them). <c>SharedKernel</c>/<c>BuildingBlocks*</c>/<c>Api</c>
/// are library/host assemblies, exempt by construction rather than by exception.
///
/// Vacuous today — no <c>.Domain</c>/<c>.Application</c>/<c>.Infrastructure</c> assembly exists yet.
/// Becomes non-vacuous the moment the first one does (G1-02).
/// </summary>
public class Rule4PublicSurfaceTests
{
    [Fact]
    public void OnlyMigrationTypesMayBePublicOutsideContracts()
    {
        var scoped = ModuleAssemblies.WithSuffix(".Domain")
            .Concat(ModuleAssemblies.WithSuffix(".Application"))
            .Concat(ModuleAssemblies.WithSuffix(".Infrastructure"));

        foreach (var assembly in scoped)
        {
            var offenders = ModuleAssemblies.SafeGetTypes(assembly)
                .Where(t => (t.IsPublic || t.IsNestedPublic) && !IsGeneratedMigrationType(t))
                .Select(t => t.FullName)
                .ToList();

            Assert.True(offenders.Count == 0,
                $"{assembly.GetName().Name} exposes public types outside Contracts: {string.Join(", ", offenders)}");
        }
    }

    private static bool IsGeneratedMigrationType(Type type) =>
        typeof(Migration).IsAssignableFrom(type) || typeof(ModelSnapshot).IsAssignableFrom(type);
}
