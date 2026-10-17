using System.Reflection;
using BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Sites.Contracts;

namespace Sites.Infrastructure.Decorators;

/// <summary>
/// Mirrors <c>Crm.Infrastructure.Decorators.CrmScopeRegistration</c> exactly, including its
/// scan-the-Contracts-assembly (not Application) shape and its "skip if no handler is registered yet"
/// tolerance. Called from <c>SitesModule.Register</c> AFTER
/// <c>AddModuleCqrs&lt;SitesDbContext&gt;</c> — Scrutor's <c>Decorate</c> wraps in call order, so the
/// scope check must be added last to end up outermost, wrapping the chain <c>AddModuleCqrs</c> already
/// built. A scope check that ran after validation would report on a payload the caller was never
/// entitled to have validated.
/// </summary>
internal static class SiteScopeRegistration
{
    public static IServiceCollection AddSiteScopeDecorator(this IServiceCollection services, Assembly assembly)
    {
        IEnumerable<Type> scopedTypes = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(ISiteScoped).IsAssignableFrom(t));

        foreach (Type? type in scopedTypes)
        {
            if (typeof(ICommand).IsAssignableFrom(type))
            {
                DecorateIfRegistered(services, typeof(ICommandHandler<>).MakeGenericType(type),
                    typeof(SiteScopeDecorator.CommandHandler<>).MakeGenericType(type));
                continue;
            }

            Type? commandInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
            if (commandInterface is not null)
            {
                Type tResponse = commandInterface.GetGenericArguments()[0];
                DecorateIfRegistered(services, typeof(ICommandHandler<,>).MakeGenericType(type, tResponse),
                    typeof(SiteScopeDecorator.CommandHandler<,>).MakeGenericType(type, tResponse));
                continue;
            }

            Type? queryInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));
            if (queryInterface is not null)
            {
                Type tResponse = queryInterface.GetGenericArguments()[0];
                DecorateIfRegistered(services, typeof(IQueryHandler<,>).MakeGenericType(type, tResponse),
                    typeof(SiteScopeDecorator.QueryHandler<,>).MakeGenericType(type, tResponse));
            }
        }

        return services;
    }

    /// <summary>Some <see cref="ISiteScoped"/> types have no handler yet at the point this runs —
    /// Scrutor's <c>Decorate</c> throws if the target service isn't registered, so this skips those
    /// rather than failing module registration. That tolerance is what lets the decorator be wired
    /// before its first real consumer exists, exactly as CRM did at G9-09.</summary>
    private static void DecorateIfRegistered(IServiceCollection services, Type serviceType, Type decoratorType)
    {
        if (services.Any(d => d.ServiceType == serviceType))
        {
            services.Decorate(serviceType, decoratorType);
        }
    }
}
