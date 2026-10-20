using Xunit;

namespace Architecture.Tests;

/// <summary>
/// <b>Rule 14: layer direction, read from the csproj files.</b>
///
/// <para>Within a module, <c>X.Infrastructure</c> never references <c>X.Application</c>. The other
/// direction is allowed and is the norm today: all nine Application projects reference their own
/// Infrastructure, because handlers use the module's DbContext directly (no repositories). CLAUDE.md
/// decides four projects per module (F2) but not that the reference is mandatory, so this rule only
/// permits it. The <c>InternalsVisibleTo Include="X.Application"</c> lines in the Infrastructure projects
/// are not references and are not read.</para>
///
/// <para>Across modules, no project references another module's Application or Infrastructure. Only
/// <c>*.Contracts</c> crosses a boundary (Rule 3 makes the same point by type; this one catches the
/// reference before any type uses it). Only <c>Identity.Contracts</c> was pinned this way before, by
/// <see cref="IdentityContractsIsolationTests"/>.</para>
/// </summary>
public class Rule14LayerDirectionTests
{
    [Fact]
    public void Rule14_InfrastructureNeverReferencesApplication_AndNoModuleReferencesAnothersInnerLayers()
    {
        List<string> csprojs = [.. ProjectFiles.ModuleCsprojs()];
        int infrastructureProjects = 0;
        List<string> violations = [];

        foreach (string csproj in csprojs)
        {
            string project = Path.GetFileNameWithoutExtension(csproj);
            string module = ModuleAssemblies.ModuleOf(project);
            bool isInfrastructure = project.EndsWith(".Infrastructure", StringComparison.Ordinal);
            if (isInfrastructure)
            {
                infrastructureProjects++;
            }

            foreach (string reference in ProjectFiles.ReferencedProjects(csproj))
            {
                bool inner = reference.EndsWith(".Application", StringComparison.Ordinal)
                    || reference.EndsWith(".Infrastructure", StringComparison.Ordinal);
                string referencedModule = ModuleAssemblies.ModuleOf(reference);

                if (isInfrastructure && reference == $"{module}.Application")
                {
                    violations.Add($"{project} -> {reference} (Infrastructure must not depend on Application)");
                }
                else if (inner && referencedModule != module && !reference.StartsWith("BuildingBlocks", StringComparison.Ordinal))
                {
                    violations.Add($"{project} -> {reference} (another module's inner layer; use its Contracts)");
                }
            }
        }

        // LIVENESS: nine modules, so nine Infrastructure projects read.
        Assert.True(infrastructureProjects >= 9,
            $"Read only {infrastructureProjects} *.Infrastructure projects from the solution, expected at least 9.");
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }
}
