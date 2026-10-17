using System.Reflection;
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
        var scoped = ModuleAssemblies.WithSuffix(".Application").Concat(ModuleAssemblies.WithSuffix(".Domain")).ToList();

        // PC-03 liveness: an empty scope runs the loop zero times and reports compliance.
        Assert.True(scoped.Count >= 18, $"Discovered only {scoped.Count} Application/Domain assemblies, expected at least 18.");

        foreach (Assembly? assembly in scoped)
        {
            bool referencesWeb = assembly.GetReferencedAssemblies().Any(a => a.Name == "BuildingBlocks.Web");

            Assert.False(referencesWeb, $"{assembly.GetName().Name} references BuildingBlocks.Web");
        }
    }

    [Fact]
    public void BuildingBlocks_MustNotReferenceAspNetCore()
    {
        string csprojPath = Path.Combine(SolutionRoot.Path, "src", "BuildingBlocks", "BuildingBlocks.csproj");
        string content = File.ReadAllText(csprojPath);

        // PC-03 liveness: prove the file was actually read and is the project file we mean. An empty or
        // wrong-path read makes "contains no AspNetCore" true for the wrong reason.
        Assert.Contains("<Project", content, StringComparison.Ordinal);

        Assert.DoesNotContain("AspNetCore", content, StringComparison.OrdinalIgnoreCase);
    }
}
