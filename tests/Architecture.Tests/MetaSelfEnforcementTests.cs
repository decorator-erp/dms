using System.Xml.Linq;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Every rule that scans <see cref="ModuleAssemblies"/> only sees what's referenced by
/// <c>Architecture.Tests.csproj</c> — a module built without adding that reference stays invisible,
/// and every rule constraining it silently passes vacuously instead of failing. That is a fail-open
/// failure mode in the one place that must fail closed.
///
/// This test closes it: it parses <c>DMS.Modular.slnx</c> directly (not <c>ModuleAssemblies</c>, which
/// would make it check itself) for every project under <c>src/Modules/</c> whose name ends in
/// <c>.Domain</c>/<c>.Application</c>/<c>.Infrastructure</c>/<c>.Contracts</c>, and asserts each one's
/// assembly was actually discovered. Forgetting the reference now fails this test, rather than leaving
/// every other rule silently green.
/// </summary>
public class MetaSelfEnforcementTests
{
    [Fact]
    public void EveryModuleProjectInTheSolution_IsReferencedByArchitectureTests()
    {
        var solutionPath = Path.Combine(SolutionRoot.Path, "DMS.Modular.slnx");
        var doc = XDocument.Load(solutionPath);

        var suffixes = new[] { ".Domain", ".Application", ".Infrastructure", ".Contracts" };

        var moduleProjectNames = doc.Descendants("Project")
            .Select(e => e.Attribute("Path")?.Value)
            .Where(path => path is not null && path.Replace('\\', '/').StartsWith("src/Modules/", StringComparison.Ordinal))
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(name => suffixes.Any(s => name!.EndsWith(s, StringComparison.Ordinal)))
            .ToList();

        // PC-03 liveness — and this one guards the guard. Everything below depends on the XML parse
        // above having found something: if `Path` were renamed, the layout moved out of src/Modules/,
        // or .slnx replaced by .sln, `moduleProjectNames` would be empty, `missing` would be empty, and
        // THIS test — the one whose entire job is to stop other tests passing vacuously — would itself
        // pass while scanning nothing. Nine modules x four projects; the floor rises as modules land.
        Assert.True(moduleProjectNames.Count >= 36,
            $"Parsed only {moduleProjectNames.Count} module projects from DMS.Modular.slnx, expected at "
            + "least 36. The parse is broken, so this test is not checking anything.");

        var discoveredNames = ModuleAssemblies.All.Select(a => a.GetName().Name).ToHashSet();

        var missing = moduleProjectNames.Where(name => !discoveredNames.Contains(name)).ToList();

        Assert.True(missing.Count == 0,
            "These module projects exist in DMS.Modular.slnx but are not referenced by "
            + $"Architecture.Tests.csproj, so every rule scanning them is silently vacuous: {string.Join(", ", missing)}");
    }
}
