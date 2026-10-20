using System.Reflection;
using BuildingBlocks.Events;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// <b>Rule 16: every integration event has a publisher.</b> Some <c>src</c> assembly other than the
/// event's own Contracts must construct it (an IL <c>newobj</c> of one of its constructors). An event
/// nobody raises is a contract other modules can subscribe to and never hear from;
/// <c>UserDeactivatedIntegrationEvent</c> was one, until it was deleted (<c>2b90a38</c>).
///
/// <para>IL rather than source text: a search for <c>new XxxIntegrationEvent(</c> misses target-typed
/// <c>new(...)</c> and finds a fully qualified <c>new Finance.Contracts.Xxx(...)</c> only by luck. Only
/// <c>src</c> assemblies are read, so a test constructing an event does not count as its
/// publisher.</para>
/// </summary>
public class Rule16IntegrationEventPublisherTests
{
    [Fact]
    public void Rule16_EveryIntegrationEvent_IsConstructedOutsideItsOwnContracts()
    {
        List<Type> events = [.. ModuleAssemblies.All
            .SelectMany(ModuleAssemblies.SafeGetTypes)
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IIntegrationEvent).IsAssignableFrom(t))
            .Distinct()];

        // LIVENESS: 29 integration events when this rule was written.
        Assert.True(events.Count >= 29, $"Found only {events.Count} integration events, expected at least 29.");

        // Every event constructed anywhere in src, with the assembly that constructs it.
        HashSet<(string Event, string By)> constructed = [.. IlAssemblies.AllSource
            .Where(m => !m.Assembly.Name.Name.EndsWith("Tests", StringComparison.Ordinal))
            .SelectMany(module => module.GetTypes()
                .SelectMany(AllTypes)
                .SelectMany(t => t.Methods)
                .Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions)
                .Where(i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference)
                .Select(i => (((MethodReference)i.Operand).DeclaringType.FullName, module.Assembly.Name.Name)))];

        List<string> unpublished = [.. events
            .Where(e => !constructed.Any(c => c.Event == e.FullName && c.By != e.Assembly.GetName().Name))
            .Select(e => e.FullName!)
            .Order(StringComparer.Ordinal)];

        Assert.True(unpublished.Count == 0,
            "These integration events are never constructed outside their own Contracts, so nothing ever "
            + $"publishes them. Raise them, or delete them: {string.Join(", ", unpublished)}");
    }

    private static IEnumerable<TypeDefinition> AllTypes(TypeDefinition type) =>
        type.NestedTypes.SelectMany(AllTypes).Prepend(type);
}
