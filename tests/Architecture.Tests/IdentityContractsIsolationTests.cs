using System.Xml.Linq;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// <b>Identity is a sink: every other module may reference it, and it references none of them.</b>
///
/// <para>Nothing enforced this before. <see cref="Rule3ModuleIsolationTests"/> forbids reaching into
/// another module's Domain/Application/Infrastructure but explicitly permits any module to reference any
/// other module's <c>*.Contracts</c> — so <c>Identity.Infrastructure</c> could take a reference on
/// <c>Hr.Contracts</c> tomorrow and no test would notice. The property lived in four csproj files and in
/// prose.</para>
///
/// <para><b>What it is holding up.</b> HR and CRM activate the logins they provision by subscribing to
/// their own integration events from their own Infrastructure assemblies, because Identity cannot
/// subscribe to theirs. <c>UnlinkedUserReaperJob</c> lives in the Api layer, not in Identity, for the
/// same reason: it must ask <c>IHrDirectory</c> and <c>IClientDirectory</c> whether a user is linked to
/// anything, and Identity may not know those exist. Move the reaper "where it belongs" and this test is
/// what says no.</para>
///
/// <para>Read from the csproj rather than through reflection, and asserted by exact set, for the reasons
/// <see cref="NotificationsContractsIsolationTests"/> records — this is that test's mirror image, one
/// direction reversed.</para>
/// </summary>
public class IdentityContractsIsolationTests
{
    /// <summary>Every project reference each Identity project is allowed to have. Intra-Identity and
    /// infrastructure-layer projects only; no entry here names another module.</summary>
    public static readonly TheoryData<string, string[]> AllowedReferences = new()
    {
        { "Identity.Contracts", ["SharedKernel", "BuildingBlocks"] },
        { "Identity.Domain", ["SharedKernel"] },
        {
            "Identity.Application",
            ["SharedKernel", "BuildingBlocks", "Identity.Contracts", "Identity.Domain", "Identity.Infrastructure"]
        },
        {
            "Identity.Infrastructure",
            ["SharedKernel", "BuildingBlocks", "BuildingBlocks.Web", "Identity.Contracts", "Identity.Domain"]
        },
    };

    [Theory]
    [MemberData(nameof(AllowedReferences))]
    public void IdentityProject_ReferencesNoOtherModule(string project, string[] allowed)
    {
        string csprojPath = Path.Combine(
            SolutionRoot.Path, "src", "Modules", "Identity", project, $"{project}.csproj");

        var referencedProjects = XDocument.Load(csprojPath)
            .Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(include => include is not null)
            .Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', '/')))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(allowed.OrderBy(name => name, StringComparer.Ordinal), referencedProjects);
    }

    /// <summary>
    /// The rule behind the lists above, stated separately so the failure message says what was actually
    /// violated rather than "these two sets differ". A reference to another module's <c>Contracts</c> is
    /// legal for every module except this one.
    /// </summary>
    [Fact]
    public void NoIdentityProject_ReferencesAnotherModulesContracts()
    {
        string identityRoot = Path.Combine(SolutionRoot.Path, "src", "Modules", "Identity");
        string[] csprojPaths = Directory.GetFiles(identityRoot, "Identity.*.csproj", SearchOption.AllDirectories);

        // LIVENESS: four projects, and a scan that found none would otherwise pass silently.
        Assert.Equal(4, csprojPaths.Length);

        var offenders = csprojPaths
            .SelectMany(path => XDocument.Load(path)
                .Descendants("ProjectReference")
                .Select(e => Path.GetFileNameWithoutExtension(
                    e.Attribute("Include")!.Value.Replace('\\', '/')))
                .Where(name => name.EndsWith(".Contracts", StringComparison.Ordinal)
                               && !name.StartsWith("Identity.", StringComparison.Ordinal))
                .Select(name => $"{Path.GetFileNameWithoutExtension(path)} -> {name}"))
            .Order()
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Identity referenced another module, so it is no longer a sink: "
            + $"{string.Join(", ", offenders)}. Every other module may reference Identity; Identity may "
            + "reference none of them. Whatever needed this belongs on the other side of the boundary "
            + "(see EmployeeHiredIntegrationEventHandler) or in the Api layer (see UnlinkedUserReaperJob).");
    }
}
