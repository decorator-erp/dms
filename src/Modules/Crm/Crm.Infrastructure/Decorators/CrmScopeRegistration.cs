using System.Reflection;
using BuildingBlocks.Messaging;
using Crm.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Crm.Infrastructure.Decorators;

/// <summary>
/// Mirrors <c>Projects.Infrastructure.Decorators.ProjectScopeRegistration</c> exactly, including its
/// scan-the-Contracts-assembly (not Application) shape and its "skip if no handler is registered yet"
/// tolerance. Called from <c>CrmModule.Register</c> at G9-09, AFTER <c>AddModuleCqrs&lt;CrmDbContext&gt;</c> —
/// Scrutor's <c>Decorate</c> wraps in call order, so the scope check must be added last to end up
/// outermost, wrapping the Validation→Logging→DomainEventDispatch chain <c>AddModuleCqrs</c> already
/// built.
/// </summary>
internal static class CrmScopeRegistration
{
    public static IServiceCollection AddCrmScopeDecorator(this IServiceCollection services, Assembly assembly)
    {
        var scopedTypes = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(ICrmScoped).IsAssignableFrom(t));

        foreach (var type in scopedTypes)
        {
            if (typeof(ICommand).IsAssignableFrom(type))
            {
                DecorateIfRegistered(services, typeof(ICommandHandler<>).MakeGenericType(type),
                    typeof(CrmScopeDecorator.CommandHandler<>).MakeGenericType(type));
                continue;
            }

            var commandInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
            if (commandInterface is not null)
            {
                var tResponse = commandInterface.GetGenericArguments()[0];
                DecorateIfRegistered(services, typeof(ICommandHandler<,>).MakeGenericType(type, tResponse),
                    typeof(CrmScopeDecorator.CommandHandler<,>).MakeGenericType(type, tResponse));
                continue;
            }

            var queryInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));
            if (queryInterface is not null)
            {
                var tResponse = queryInterface.GetGenericArguments()[0];
                DecorateIfRegistered(services, typeof(IQueryHandler<,>).MakeGenericType(type, tResponse),
                    typeof(CrmScopeDecorator.QueryHandler<,>).MakeGenericType(type, tResponse));
            }
        }

        return services;
    }

    /// <summary>Some <see cref="ICrmScoped"/> types don't have a handler built yet at the point this
    /// runs — Scrutor's <c>Decorate</c> throws if the target service isn't registered, so this skips
    /// those rather than failing module registration. Every type WITH a handler still gets decorated
    /// normally.</summary>
    private static void DecorateIfRegistered(IServiceCollection services, Type serviceType, Type decoratorType)
    {
        if (services.Any(d => d.ServiceType == serviceType))
            services.Decorate(serviceType, decoratorType);
    }
}
