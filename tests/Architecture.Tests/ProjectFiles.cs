using System.Xml.Linq;

namespace Architecture.Tests;

/// <summary>
/// Reads the solution and project files as text, for the rules that are about how the projects are
/// wired rather than about the code in them (Rules 14, 15, 17 and 18).
/// </summary>
internal static class ProjectFiles
{
    /// <summary>Every project path listed in <c>DMS.Modular.slnx</c>, with forward slashes.</summary>
    public static IReadOnlyList<string> SolutionProjects() =>
        [.. XDocument.Load(Path.Combine(SolutionRoot.Path, "DMS.Modular.slnx"))
            .Descendants("Project")
            .Select(e => e.Attribute("Path")?.Value)
            .Where(path => path is not null)
            .Select(path => path!.Replace('\\', '/'))];

    /// <summary>The module folder names under <c>src/Modules/</c> that the solution lists projects from.</summary>
    public static IReadOnlyList<string> ModuleNames() =>
        [.. SolutionProjects()
            .Where(path => path.StartsWith("src/Modules/", StringComparison.Ordinal))
            .Select(path => path.Split('/')[2])
            .Distinct()
            .Order(StringComparer.Ordinal)];

    /// <summary>The <c>src/Modules</c> csproj paths in the solution, absolute.</summary>
    public static IReadOnlyList<string> ModuleCsprojs() =>
        [.. SolutionProjects()
            .Where(path => path.StartsWith("src/Modules/", StringComparison.Ordinal))
            .Select(path => Path.Combine(SolutionRoot.Path, path))];

    /// <summary>The project names a csproj's <c>ProjectReference</c> items point at. Only
    /// <c>ProjectReference</c>: an <c>InternalsVisibleTo</c> naming another project is not a reference.</summary>
    public static IReadOnlyList<string> ReferencedProjects(string csprojPath) =>
        [.. XDocument.Load(csprojPath)
            .Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(include => include is not null)
            .Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', '/')))];
}
