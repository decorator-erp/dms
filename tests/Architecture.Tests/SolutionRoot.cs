namespace Architecture.Tests;

/// <summary>
/// Locates the repository root (where <c>DMS.Modular.slnx</c> lives) by walking up from the test
/// assembly's own output directory. Stable regardless of Debug/Release or the test runner's working
/// directory — used by rules that need to read a csproj file directly rather than reflect on a loaded
/// assembly.
/// </summary>
internal static class SolutionRoot
{
    private static readonly Lazy<string> Value = new(Find);

    public static string Path => Value.Value;

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "DMS.Modular.slnx")))
            dir = dir.Parent;

        return dir?.FullName
            ?? throw new InvalidOperationException(
                $"Could not locate DMS.Modular.slnx above '{AppContext.BaseDirectory}'.");
    }
}
