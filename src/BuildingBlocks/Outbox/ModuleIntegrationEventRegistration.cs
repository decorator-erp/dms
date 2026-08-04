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
/// the module's own assembly, never via Scrutor's open-generic Decorate — which would rewrap every
/// other module's handlers too.
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

        services.Scan(scan => scan.FromAssemblies(assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(IIntegrationEventHandler<>)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime());

        foreach (var implementationType in assembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false }))
        {
            foreach (var handlerInterface in implementationType.GetInterfaces().Where(i => i.IsGenericType))
            {
                if (handlerInterface.GetGenericTypeDefinition() != typeof(IIntegrationEventHandler<>))
                    continue;

                var tEvent = handlerInterface.GetGenericArguments()[0];
                var serviceType = typeof(IIntegrationEventHandler<>).MakeGenericType(tEvent);
                var decoratorType = typeof(InboxDedupeDecorator<,>).MakeGenericType(typeof(TContext), tEvent);

                services.Decorate(serviceType, decoratorType);
            }
        }

        return services;
    }
}
