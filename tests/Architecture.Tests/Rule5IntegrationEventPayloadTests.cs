using BuildingBlocks.Events;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Rule 5: <see cref="IIntegrationEvent"/> implementations expose only primitives, <see cref="Guid"/>,
/// <see cref="DateTimeOffset"/>, <see cref="string"/>, enums, or collections thereof — payloads must be
/// safe to serialize across a module (and, eventually, process) boundary with no risk of leaking a rich
/// domain object.
///
/// Vacuous today — no integration event beyond the interface itself exists. Becomes non-vacuous at
/// G1-01 (<c>UserRegisteredIntegrationEvent</c>, <c>UserDeactivatedIntegrationEvent</c>).
/// </summary>
public class Rule5IntegrationEventPayloadTests
{
    [Fact]
    public void IntegrationEvents_ExposeOnlyPrimitiveShapedPayloads()
    {
        var eventTypes = ModuleAssemblies.All
            .SelectMany(ModuleAssemblies.SafeGetTypes)
            .Where(t => typeof(IIntegrationEvent).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false });

        foreach (var type in eventTypes)
        {
            foreach (var property in type.GetProperties())
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
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying.IsEnum || underlying.IsPrimitive)
            return true;

        if (underlying == typeof(string) || underlying == typeof(Guid) || underlying == typeof(DateTimeOffset)
            || underlying == typeof(DateTime) || underlying == typeof(decimal))
            return true;

        if (underlying.IsArray)
            return IsAllowedPayloadType(underlying.GetElementType()!);

        if (underlying.IsGenericType)
        {
            var definition = underlying.GetGenericTypeDefinition();
            if (definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>)
                || definition == typeof(IEnumerable<>) || definition == typeof(List<>))
            {
                return IsAllowedPayloadType(underlying.GetGenericArguments()[0]);
            }
        }

        return false;
    }
}
