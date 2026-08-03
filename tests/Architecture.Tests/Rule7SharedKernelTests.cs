using System.Xml.Linq;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Rule 7: <c>SharedKernel</c> has zero project references. The only rule live from day one — no
/// module needs to exist for it to mean something. Read directly from the csproj rather than via
/// reflection over referenced assemblies, since the runtime referenced-assembly list can't distinguish
/// "no project references" from "references only framework assemblies" as precisely as the build file
/// itself does.
/// </summary>
public class Rule7SharedKernelTests
{
    [Fact]
    public void SharedKernel_HasZeroProjectReferences()
    {
        var csprojPath = Path.Combine(SolutionRoot.Path, "src", "SharedKernel", "SharedKernel.csproj");
        var doc = XDocument.Load(csprojPath);

        var projectReferences = doc.Descendants("ProjectReference").ToList();

        Assert.True(projectReferences.Count == 0,
            $"SharedKernel.csproj has {projectReferences.Count} ProjectReference element(s), expected 0");
    }
}
