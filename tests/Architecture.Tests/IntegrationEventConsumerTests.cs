using BuildingBlocks.Events;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Every <see cref="IIntegrationEvent"/> either has a handler, or appears in
/// <see cref="DeclaredUnconsumed"/> with a reason. Nothing sits between the two.
///
/// <para><b>Why this is a test and not a convention.</b> Publishing with zero registered handlers is a
/// supported no-op — R2 marks the event processed — so an unconsumed event breaks nothing and shows up
/// nowhere. The repo's answer since S1-07 was to state it in the event's own doc comment, which worked
/// exactly as long as nobody changed anything: X-02 found
/// <c>SiteApprovedIntegrationEvent</c> still reading "No subscriber exists in this repo" two phases
/// after <c>SiteApprovedNotificationHandler</c> started consuming it, and found eight unconsumed events
/// where the plan that opened the task assumed two. Prose cannot notice that it has stopped being
/// true.</para>
///
/// <para><b>Reflect over assemblies, do not resolve from the container.</b> N1-08's
/// <c>NotificationEventConsumerTests</c> asked the container for handlers and filtered on
/// <c>GetType().Name</c>; <c>AddModuleIntegrationEvents</c> wraps every handler in
/// <c>InboxDedupeDecorator&lt;,&gt;</c>, so the name was always <c>InboxDedupeDecorator`2</c> and the
/// filter matched nothing, ever. No container wrapping can disguise a type's implemented
/// interfaces.</para>
/// </summary>
public class IntegrationEventConsumerTests
{
    /// <summary>
    /// Events published with no handler, deliberately. Each line is a decision someone made; a name
    /// here is not a TODO, it is a record that the question was asked and answered.
    ///
    /// <para><b>This list is not allowed to grow silently, and not allowed to go stale.</b> Both
    /// directions are asserted below.</para>
    /// </summary>
    private static readonly HashSet<string> DeclaredUnconsumed =
    [
        // Sites models no reviewer role and no assignment, so there is no one to tell that a survey
        // reached review. Recorded at N1-06, restated on the type itself.
        "SiteSubmittedIntegrationEvent",

        // Crm announces the conversion; nothing downstream acts on it yet. Projects learns about the
        // resulting project through its own creation path, not through this.
        "LeadConvertedIntegrationEvent",

        // Projects publishes the full task lifecycle for symmetry. Assigned/Completed/Rejected/Reopened
        // have notification handlers; Started and Cancelled are the two nobody asked to be told about.
        "ProjectTaskStartedIntegrationEvent",
        "ProjectTaskCancelledIntegrationEvent",

        // Gate satisfaction is acted on in-module, inside the same transaction that satisfies it. This
        // event exists for a future subscriber that wants the fact without the mechanism.
        "GateConditionSatisfiedIntegrationEvent",

        // UserRegisteredIntegrationEvent and UserDeactivatedIntegrationEvent used to be listed here.
        // Both are deleted: the first went with self-registration (nothing announces registration
        // because nothing registers), and the second was the entry this list itself called "the first
        // thing to delete for whoever sweeps next" -- defined, never published, never consumed.
        // Identity.Contracts now declares no integration events at all, which is what a sink should
        // look like.
    ];

    [Fact]
    public void EveryIntegrationEvent_HasAHandler_OrIsDeclaredUnconsumed()
    {
        HashSet<string> events = IntegrationEventNames();
        HashSet<string> handled = HandledEventNames();

        // LIVENESS (Shape 1). Both collectors are reflection scans, so both fail silent: an empty
        // `events` set makes direction A vacuous, and an empty `handled` set makes every event look
        // unconsumed. Named types rather than bare NotEmpty, so a scan that returns *something*
        // unrelated cannot satisfy this either.
        Assert.Contains("TaskBudgetCoverageChangedIntegrationEvent", events);
        Assert.Contains("TaskBudgetCoverageChangedIntegrationEvent", handled);

        var unconsumed = events.Except(handled).ToHashSet();

        // DIRECTION A -- actual.Except(expected). This is the direction that passes green on an empty
        // result, which is what the liveness pair above is guarding. A new event published with no
        // handler fails here until someone either writes the handler or writes down why not.
        var undeclared = unconsumed.Except(DeclaredUnconsumed).Order().ToList();
        Assert.True(undeclared.Count == 0,
            "Integration events with no handler and no entry in DeclaredUnconsumed: "
            + $"{string.Join(", ", undeclared)}. Write a handler, or add the name with a one-line "
            + "reason.");

        // DIRECTION B -- expected.Except(actual), which fails loudly rather than silently. An entry
        // that has GAINED a handler is stale, and this is the assertion that would have caught
        // SiteApprovedIntegrationEvent's doc comment in Phase 6. It forces the list and the type's own
        // comment to be corrected together.
        var stale = DeclaredUnconsumed.Except(unconsumed).Order().ToList();
        Assert.True(stale.Count == 0,
            $"DeclaredUnconsumed names events that now HAVE a handler: {string.Join(", ", stale)}. "
            + "Remove them from the list, and fix whatever the type's doc comment still claims.");
    }

    /// <summary>Every concrete <see cref="IIntegrationEvent"/>, gathered as
    /// <c>Rule5IntegrationEventPayloadTests</c> gathers them.</summary>
    private static HashSet<string> IntegrationEventNames() =>
        [.. ModuleAssemblies.All
            .SelectMany(ModuleAssemblies.SafeGetTypes)
            .Where(t => typeof(IIntegrationEvent).IsAssignableFrom(t)
                        && t is { IsAbstract: false, IsInterface: false })
            .Select(t => t.Name)];

    /// <summary>
    /// The event type behind every <c>IIntegrationEventHandler&lt;T&gt;</c> implementation. Handlers
    /// live in <c>*.Infrastructure</c> by convention — <c>AddModuleIntegrationEvents</c> scans that
    /// assembly — so scanning wider would only find the decorator in BuildingBlocks, whose <c>T</c> is
    /// an open generic parameter and names no event.
    /// </summary>
    private static HashSet<string> HandledEventNames() =>
        [.. ModuleAssemblies.WithSuffix(".Infrastructure")
            .SelectMany(ModuleAssemblies.SafeGetTypes)
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .SelectMany(t => t.GetInterfaces())
            .Where(i => i.IsGenericType
                        && i.GetGenericTypeDefinition() == typeof(IIntegrationEventHandler<>))
            .Select(i => i.GetGenericArguments()[0])
            .Where(t => !t.IsGenericParameter)
            .Select(t => t.Name)];
}
