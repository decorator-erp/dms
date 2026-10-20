using System.Text.RegularExpressions;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// <b>Rule 17: the host loads every module, and only modules.</b> <c>Api/Program.cs</c> loads each
/// module by assembly name (<c>Assembly.Load("X.Infrastructure")</c>, since the IModule types are
/// internal), and the list is hand-kept. A module added to the solution but not to that list builds,
/// passes its own tests, and is never registered by the real host.
/// </summary>
public partial class Rule17ModuleListTests
{
    [GeneratedRegex("""Assembly\.Load\("(?<module>[A-Za-z]+)\.Infrastructure"\)""")]
    private static partial Regex LoadCall();

    [Fact]
    public void Rule17_ProgramLoadsExactlyTheModulesInTheSolution()
    {
        string program = File.ReadAllText(Path.Combine(SolutionRoot.Path, "src", "Api", "Program.cs"));
        List<string> loaded = [.. LoadCall().Matches(program).Select(m => m.Groups["module"].Value).Order(StringComparer.Ordinal)];
        IReadOnlyList<string> modules = ProjectFiles.ModuleNames();

        // LIVENESS: both sides must have been read.
        Assert.True(modules.Count >= 9, $"Read only {modules.Count} module folders from the solution, expected at least 9.");
        Assert.True(loaded.Count >= 9, $"Found only {loaded.Count} Assembly.Load calls in Program.cs, expected at least 9.");

        Assert.Equal(modules, loaded);
    }
}
