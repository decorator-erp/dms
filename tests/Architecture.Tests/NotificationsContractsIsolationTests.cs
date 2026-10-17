using System.Xml.Linq;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// <b>Notifications is a hub, not a sink — and this is the one thing keeping that buildable.</b>
///
/// <para>Seven modules will reference <c>Notifications.Contracts</c>: their periodic jobs
/// (<c>IPeriodicJob</c>, N1-05) and their notification handlers (N1-06) call
/// <c>INotificationService</c>. That is the one allowed outbound direction. It only works while
/// <c>Notifications.Contracts</c> itself depends on no module — the moment it references, say,
/// <c>Hr.Contracts</c>, the next module to reference Notifications closes a reference cycle and the
/// solution stops building, with an error that names two projects and explains neither.</para>
///
/// <para>Read from the csproj rather than through reflection, for the same reason
/// <see cref="Rule7SharedKernelTests"/> does: the runtime referenced-assembly list cannot distinguish
/// "no project references" from "references only framework assemblies" as precisely as the build file
/// itself does. Asserted by exact count and by name, so both adding a reference and swapping one go
/// red.</para>
///
/// <para>Written at N1-01, before any module references this project, precisely so the guard exists
/// before the pressure does.</para>
/// </summary>
public class NotificationsContractsIsolationTests
{
    private static readonly string[] AllowedReferences = ["SharedKernel", "BuildingBlocks"];

    [Fact]
    public void NotificationsContracts_ReferencesSharedKernelAndBuildingBlocksOnly()
    {
        string csprojPath = Path.Combine(
            SolutionRoot.Path, "src", "Modules", "Notifications",
            "Notifications.Contracts", "Notifications.Contracts.csproj");
        var doc = XDocument.Load(csprojPath);

        var referencedProjects = doc.Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(include => include is not null)
            .Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', '/')))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            AllowedReferences.OrderBy(name => name, StringComparer.Ordinal),
            referencedProjects);
    }
}
