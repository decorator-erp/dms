using BuildingBlocks.Messaging;
using Projects.Contracts;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// P10 (C5): every <see cref="IProjectScoped"/> command declares the project level it needs, through
/// <see cref="IRequiresProjectEditor"/> or <see cref="IRequiresProjectManager"/>. <c>ProjectScopeDecorator</c>
/// checks that level after scope (404) and before the handler's state rules (409); a command without one would
/// run at whatever level scope alone allows, which is Viewer. Queries need only scope and declare nothing.
/// </summary>
public class ProjectLevelDeclarationTests
{
    [Fact]
    public void EveryProjectScopedCommand_DeclaresARequiredLevel()
    {
        System.Reflection.Assembly contracts = typeof(IProjectScoped).Assembly;
        List<Type> commands = [.. ModuleAssemblies.SafeGetTypes(contracts)
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IProjectScoped).IsAssignableFrom(t))
            .Where(IsCommand)];

        // LIVENESS: Projects has dozens of scoped commands; an empty scan would pass vacuously.
        Assert.True(commands.Count >= 40, $"expected at least 40 IProjectScoped commands, found {commands.Count}");

        List<string> undeclared = [.. commands
            .Where(t => !typeof(IRequiresProjectEditor).IsAssignableFrom(t) && !typeof(IRequiresProjectManager).IsAssignableFrom(t))
            .Select(t => t.FullName!)];

        Assert.True(undeclared.Count == 0,
            "IProjectScoped commands must declare IRequiresProjectEditor or IRequiresProjectManager: " + string.Join(", ", undeclared));
    }

    [Fact]
    public void NoProjectScopedQuery_DeclaresALevel()
    {
        List<string> queries = [.. ModuleAssemblies.SafeGetTypes(typeof(IProjectScoped).Assembly)
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IProjectScoped).IsAssignableFrom(t))
            .Where(t => !IsCommand(t) && typeof(IRequiresProjectLevel).IsAssignableFrom(t))
            .Select(t => t.FullName!)];

        Assert.True(queries.Count == 0, "queries are Viewer by scope alone; the decorator ignores a level on them: " + string.Join(", ", queries));
    }

    private static bool IsCommand(Type type) =>
        typeof(ICommand).IsAssignableFrom(type)
        || type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
}
