using Xunit;

namespace Architecture.Tests;

/// <summary>
/// <b>Rule 18: every module has its three test projects in the solution.</b> <c>X.UnitTests</c> for the
/// Domain, <c>X.Application.Tests</c> for handlers against a real database, and
/// <c>X.Infrastructure.Tests</c> for the DbContext and the wiring. Approvals had no Infrastructure.Tests
/// and Notifications no Application.Tests until Phase 5; a missing project means a layer nothing tests
/// in isolation.
/// </summary>
public class Rule18ModuleTestProjectsTests
{
    private static readonly string[] Kinds = ["UnitTests", "Application.Tests", "Infrastructure.Tests"];

    [Fact]
    public void Rule18_EveryModuleHasUnitApplicationAndInfrastructureTestProjects()
    {
        HashSet<string> testProjects = [.. ProjectFiles.SolutionProjects()
            .Where(path => path.StartsWith("tests/Modules/", StringComparison.Ordinal))
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()];
        IReadOnlyList<string> modules = ProjectFiles.ModuleNames();

        List<string> expected = [.. modules.SelectMany(module => Kinds.Select(kind => $"{module}.{kind}"))];

        // LIVENESS: nine modules, three projects each.
        Assert.True(expected.Count >= 27, $"Expected at least 27 module test projects, derived only {expected.Count}.");

        List<string> missing = [.. expected.Where(name => !testProjects.Contains(name))];
        Assert.True(missing.Count == 0,
            $"These module test projects are missing from tests/Modules in the solution: {string.Join(", ", missing)}");
    }
}
