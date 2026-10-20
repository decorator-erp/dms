using Xunit;

namespace Architecture.Tests;

/// <summary>
/// <b>Rule 15: every <c>X.Contracts</c> references SharedKernel and BuildingBlocks and nothing else.</b>
/// A Contracts project is what other modules compile against; one that referenced its own Domain, or
/// another module, would hand that dependency to every consumer. Identity's and Notifications' own
/// isolation tests stay; this states the rule once for all nine.
/// </summary>
public class Rule15ContractsIsolationTests
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal) { "SharedKernel", "BuildingBlocks" };

    [Fact]
    public void Rule15_ContractsProjects_ReferenceOnlySharedKernelAndBuildingBlocks()
    {
        List<string> contracts = [.. ProjectFiles.ModuleCsprojs()
            .Where(p => Path.GetFileNameWithoutExtension(p).EndsWith(".Contracts", StringComparison.Ordinal))];

        // LIVENESS: one Contracts project per module.
        Assert.True(contracts.Count >= 9, $"Read only {contracts.Count} *.Contracts projects, expected at least 9.");

        List<string> violations = [.. contracts.SelectMany(csproj => ProjectFiles.ReferencedProjects(csproj)
            .Where(reference => !Allowed.Contains(reference))
            .Select(reference => $"{Path.GetFileNameWithoutExtension(csproj)} -> {reference}"))];

        Assert.True(violations.Count == 0,
            $"A Contracts project referenced more than SharedKernel and BuildingBlocks: {string.Join(", ", violations)}");
    }
}
