using System.Reflection;
using BuildingBlocks.Events;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Rule 5: <see cref="IIntegrationEvent"/> implementations expose only primitives, <see cref="Guid"/>,
/// <see cref="DateTimeOffset"/>, <see cref="string"/>, enums, or collections thereof — payloads must be
/// safe to serialize across a module (and, eventually, process) boundary with no risk of leaking a rich
/// domain object.
///
/// Was vacuous when written — no integration event beyond the interface itself existed yet. The two
/// Identity events that first made it non-vacuous have since been deleted along with self-registration;
/// the <c>Assert.NotEmpty</c> below is what keeps that from silently returning it to vacuity.
/// </summary>
public class Rule5IntegrationEventPayloadTests
{
    [Fact]
    public void IntegrationEvents_ExposeOnlyPrimitiveShapedPayloads()
    {
        var eventTypes = ModuleAssemblies.All
            .SelectMany(ModuleAssemblies.SafeGetTypes)
            .Where(t => typeof(IIntegrationEvent).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
            .ToList();

        // PC-03 liveness. The only assertion in this test lives inside the loop below, so zero
        // discovered event types means zero assertions executed and a green result — the rule would
        // report "every payload is primitive-shaped" having examined no payload at all. Unlike rules
        // 1/4/6/8a this one was never fault-injected, so nothing had ever shown it capable of failing.
        Assert.NotEmpty(eventTypes);

        foreach (Type? type in eventTypes)
        {
            foreach (PropertyInfo property in type.GetProperties())
            {
                Assert.True(IsAllowedPayloadType(property.PropertyType),
                    $"{type.FullName}.{property.Name} ({property.PropertyType}) is not an allowed " +
                    "integration-event payload type (primitives, Guid, DateTimeOffset, string, enums, " +
                    "or collections thereof only)");
            }
        }
    }

    private static bool IsAllowedPayloadType(Type type)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying.IsEnum || underlying.IsPrimitive)
        {
            return true;
        }

        if (underlying == typeof(string) || underlying == typeof(Guid) || underlying == typeof(DateTimeOffset)
            || underlying == typeof(DateTime) || underlying == typeof(decimal))
        {
            return true;
        }

        if (underlying.IsArray)
        {
            return IsAllowedPayloadType(underlying.GetElementType()!);
        }

        if (underlying.IsGenericType)
        {
            Type definition = underlying.GetGenericTypeDefinition();
            if (definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>)
                || definition == typeof(IEnumerable<>) || definition == typeof(List<>))
            {
                return IsAllowedPayloadType(underlying.GetGenericArguments()[0]);
            }
        }

        return false;
    }
}
