using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Rule 8: no <c>*.Application</c> or <c>*.Domain</c> project references <c>BuildingBlocks.Web</c>,
/// and <c>BuildingBlocks</c> itself references no <c>Microsoft.AspNetCore.*</c>.
///
/// The second half is live today (<c>BuildingBlocks</c> exists now); the first half is vacuous until a
/// module's <c>.Application</c>/<c>.Domain</c> assembly exists (G1-02 / G1-05).
/// </summary>
public class Rule8BuildingBlocksWebTests
{
    [Fact]
    public void ApplicationAndDomainAssemblies_MustNotReferenceBuildingBlocksWeb()
    {
        foreach (var assembly in ModuleAssemblies.WithSuffix(".Application").Concat(ModuleAssemblies.WithSuffix(".Domain")))
        {
            var referencesWeb = assembly.GetReferencedAssemblies().Any(a => a.Name == "BuildingBlocks.Web");

            Assert.False(referencesWeb, $"{assembly.GetName().Name} references BuildingBlocks.Web");
        }
    }

    [Fact]
    public void BuildingBlocks_MustNotReferenceAspNetCore()
    {
        var csprojPath = Path.Combine(SolutionRoot.Path, "src", "BuildingBlocks", "BuildingBlocks.csproj");
        var content = File.ReadAllText(csprojPath);

        Assert.DoesNotContain("AspNetCore", content, StringComparison.OrdinalIgnoreCase);
    }
}
