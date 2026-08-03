using System.Reflection;
using BuildingBlocks.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
