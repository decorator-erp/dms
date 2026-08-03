using BuildingBlocks.Messaging;
using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Rule 10: commands and queries are module-private, even though they live in <c>*.Contracts</c> and are
/// therefore public. Rule 4 forced <c>ICommand</c>/<c>ICommand&lt;T&gt;</c>/<c>IQuery&lt;T&gt;</c> wrapper
/// types out of <c>*.Application</c> into <c>*.Contracts</c> to break the Application/Infrastructure
/// circular reference (see <c>docs/adding-a-module.md</c>). That resolution has a side effect Rule 3
/// alone doesn't catch: nothing stops module B from referencing module A's Contracts assembly (allowed —
/// that's what Contracts is for), naming A's command/query type, and resolving
/// <c>ICommandHandler&lt;ACommand&gt;</c> from the shared DI container — a synchronous cross-module write
/// that never touches A's Application or Infrastructure and so is invisible to Rule 3.
///
/// This rule closes that hole directly: no type in module B may depend on a type in
/// <c>A.Contracts</c> that implements <see cref="IBaseCommand"/> or <see cref="IBaseQuery"/>. DTOs,
/// contract interfaces, permission constants, and integration events in <c>A.Contracts</c> remain fair
/// game for cross-module reference — only the command/query types themselves are module-private.
///
/// Expected to pass non-vacuously today with zero findings: every endpoint constructs only its own
/// module's commands/queries (A == B, exempt by the loop below), which is exactly what the rule permits.
/// </summary>
public class Rule10CommandQueryPrivacyTests
{
    [Fact]
    public void Modules_MustNotDependOnAnotherModulesCommandOrQueryTypes()
    {
        var contractsAssemblies = ModuleAssemblies.WithSuffix(".Contracts");
        var allModuleAssemblies = ModuleAssemblies.All;

        foreach (var contractsAssembly in contractsAssemblies)
        {
            var ownModule = ModuleAssemblies.ModuleOf(contractsAssembly.GetName().Name!);

            var commandAndQueryTypeNames = ModuleAssemblies.SafeGetTypes(contractsAssembly)
                .Where(t => t is { IsClass: true, IsAbstract: false } || (t.IsValueType && !t.IsEnum))
                .Where(t => typeof(IBaseCommand).IsAssignableFrom(t) || typeof(IBaseQuery).IsAssignableFrom(t))
                .Select(t => t.FullName!)
                .ToArray();

            if (commandAndQueryTypeNames.Length == 0)
                continue;

            var otherModuleAssemblies = allModuleAssemblies
                .Where(a => ModuleAssemblies.ModuleOf(a.GetName().Name!) != ownModule);

            foreach (var assembly in otherModuleAssemblies)
            {
                var result = Types.InAssembly(assembly)
                    .ShouldNot()
                    .HaveDependencyOnAny(commandAndQueryTypeNames)
                    .GetResult();

                ArchAssert.Passes(assembly, result);
            }
        }
    }
}
