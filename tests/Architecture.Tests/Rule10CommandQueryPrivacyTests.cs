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

        // PC-03 liveness. This rule has TWO independent ways to pass without checking anything, and the
        // summary above claims non-vacuity that nothing verified: the `continue` below skips any
        // Contracts assembly whose command/query set is empty, and NetArchTest's Types.InAssembly
        // returns IsSuccessful == true for an empty type set. Counting the pairs actually evaluated —
        // and asserting the count at the end — is what makes the summary's claim checkable.
        var pairsChecked = 0;
        var commandsAndQueriesFound = 0;

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

            commandsAndQueriesFound += commandAndQueryTypeNames.Length;

            var otherModuleAssemblies = allModuleAssemblies
                .Where(a => ModuleAssemblies.ModuleOf(a.GetName().Name!) != ownModule);

            foreach (var assembly in otherModuleAssemblies)
            {
                var result = Types.InAssembly(assembly)
                    .ShouldNot()
                    .HaveDependencyOnAny(commandAndQueryTypeNames)
                    .GetResult();

                ArchAssert.Passes(assembly, result);
                pairsChecked++;
            }
        }

        // Eight modules, each with a populated Contracts assembly, checked against the other seven
        // modules' assemblies. Exact floors would be brittle as modules land; these are low enough to
        // stay true and high enough that a broken discovery path cannot slip under them.
        Assert.True(commandsAndQueriesFound >= 100,
            $"Found only {commandsAndQueriesFound} command/query types across all *.Contracts assemblies. "
            + "Discovery is broken, so every `continue` above is skipping silently.");
        Assert.True(pairsChecked >= 100,
            $"Evaluated only {pairsChecked} (contracts, foreign assembly) pairs. The rule is near-vacuous.");
    }
}
