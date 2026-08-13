using System.Reflection;
using BuildingBlocks.DomainEvents;
using BuildingBlocks.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel;

namespace BuildingBlocks.Outbox;

/// <summary>
/// Registers one module's <see cref="IIntegrationEventHandler{T}"/> implementations found in
/// <paramref name="assembly"/>, each wrapped in <see cref="InboxDedupeDecorator{TContext,TEvent}"/>.
///
/// Same reasoning as <c>BuildingBlocks.ModuleCqrsRegistration.AddModuleCqrs</c>: decoration is applied
/// per fully-closed <c>IIntegrationEventHandler&lt;TEvent&gt;</c> service type, discovered by reflecting
/// the module's own assembly.
///
/// <para>
/// <b>Not via Scrutor's <c>Decorate(serviceType, decoratorType)</c></b> — found broken at G7-07, the
/// first time two modules (Projects, since G5-08, and Documents) both register a handler for the SAME
/// event type (<c>ApprovalDecidedIntegrationEvent</c>). Scrutor's <c>Decorate</c> wraps EVERY descriptor
/// currently registered under <paramref name="serviceType"/> — not just the ones this call just added.
/// Documents' call ran after Projects' and re-wrapped Projects' ALREADY-decorated registration
/// (<c>InboxDedupeDecorator&lt;ProjectsDbContext,...&gt;</c>) in a second, outer
/// <c>InboxDedupeDecorator&lt;DocumentsDbContext,...&gt;</c> — a decorator of a decorator, using the
/// WRONG <c>TContext</c> for Projects' own handler, and whose closed-generic <c>Type.FullName</c>
/// (embedding both context assembly-qualified names) overflowed <c>InboxMessage.HandlerName</c>'s
/// <c>varchar(256)</c> column outright. Fixed by decorating only the descriptors THIS call's own
/// <see cref="IServiceCollection.Scan"/> just added (identified by list position, captured before the
/// scan), never the whole container.
/// </para>
/// </summary>
public static class ModuleIntegrationEventRegistration
{
    public static IServiceCollection AddModuleIntegrationEvents<TContext>(
        this IServiceCollection services, Assembly assembly)
        where TContext : DbContext
    {
        // TryAdd, not AddModuleCqrs's plain AddScoped: InboxDedupeDecorator now depends on
        // DomainEventDispatcher<TContext> (F1 — see that decorator's own remarks) to drain the cascade a
        // handler's write may trigger, in the same transaction as the inbox row. Every module in this
        // codebase calls AddModuleCqrs<TContext> before this method, which already registers both — these
        // TryAdd calls exist so this method is self-sufficient regardless of call order, not because the
        // established order is expected to change.
        services.TryAddScoped<IDomainEventQueue, DomainEventQueue>();
        services.TryAddScoped<DomainEventDispatcher<TContext>>();

        // InboxDedupeDecorator now nudges OutboxSignal after every commit (W1) - same self-sufficiency
        // reasoning as the two TryAdd calls above, and as AddModuleCqrs's identical TryAdd.
        services.TryAddSingleton<OutboxSignal>();

        var countBeforeScan = services.Count;

        services.Scan(scan => scan.FromAssemblies(assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(IIntegrationEventHandler<>)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime());

        // Only the descriptors THIS Scan call just added - see the type-level remarks on why the
        // alternative (Scrutor's blanket Decorate) is wrong once a second module shares an event type.
        for (var i = countBeforeScan; i < services.Count; i++)
        {
            var descriptor = services[i];
            if (!descriptor.ServiceType.IsGenericType ||
                descriptor.ServiceType.GetGenericTypeDefinition() != typeof(IIntegrationEventHandler<>) ||
                descriptor.ImplementationType is null)
                continue;

            var tEvent = descriptor.ServiceType.GetGenericArguments()[0];
            var decoratorType = typeof(InboxDedupeDecorator<,>).MakeGenericType(typeof(TContext), tEvent);
            var implementationType = descriptor.ImplementationType;

            services[i] = ServiceDescriptor.Describe(
                descriptor.ServiceType,
                sp =>
                {
                    var innerHandler = ActivatorUtilities.CreateInstance(sp, implementationType);
                    return ActivatorUtilities.CreateInstance(sp, decoratorType, innerHandler);
                },
                descriptor.Lifetime);
        }

        return services;
    }
}
