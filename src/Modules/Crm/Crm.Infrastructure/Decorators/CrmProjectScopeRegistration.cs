using System.Reflection;
using BuildingBlocks.Messaging;
using Crm.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Crm.Infrastructure.Decorators;

/// <summary>
/// <c>CrmScopeRegistration</c>'s shape for <see cref="ICrmProjectScoped"/>. Called from
/// <c>CrmModule.Register</c> after <c>AddCrmScopeDecorator</c> (itself after <c>AddModuleCqrs</c>), so
/// the project-scope check is the outermost wrapper — before validation and before the transaction.
/// </summary>
internal static class CrmProjectScopeRegistration
{
    public static IServiceCollection AddCrmProjectScopeDecorator(this IServiceCollection services, Assembly assembly) =>
        services.AddScopeDecorator(assembly, typeof(ICrmProjectScoped),
            typeof(CrmProjectScopeDecorator.CommandHandler<>), typeof(CrmProjectScopeDecorator.CommandHandler<,>),
            typeof(CrmProjectScopeDecorator.QueryHandler<,>));

    /// <summary>C-1: the same wiring for <see cref="ICrmClientScoped"/>. Called right after the project scope;
    /// no message carries both markers.</summary>
    public static IServiceCollection AddCrmClientScopeDecorator(this IServiceCollection services, Assembly assembly) =>
        services.AddScopeDecorator(assembly, typeof(ICrmClientScoped),
            typeof(CrmClientScopeDecorator.CommandHandler<>), typeof(CrmClientScopeDecorator.CommandHandler<,>),
            typeof(CrmClientScopeDecorator.QueryHandler<,>));

    private static IServiceCollection AddScopeDecorator(
        this IServiceCollection services, Assembly assembly, Type marker,
        Type commandDecorator, Type commandWithResponseDecorator, Type queryDecorator)
    {
        IEnumerable<Type> scopedTypes = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && marker.IsAssignableFrom(t));

        foreach (Type? type in scopedTypes)
        {
            if (typeof(ICommand).IsAssignableFrom(type))
            {
                DecorateIfRegistered(services, typeof(ICommandHandler<>).MakeGenericType(type),
                    commandDecorator.MakeGenericType(type));
                continue;
            }

            Type? commandInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
            if (commandInterface is not null)
            {
                Type tResponse = commandInterface.GetGenericArguments()[0];
                DecorateIfRegistered(services, typeof(ICommandHandler<,>).MakeGenericType(type, tResponse),
                    commandWithResponseDecorator.MakeGenericType(type, tResponse));
                continue;
            }

            Type? queryInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));
            if (queryInterface is not null)
            {
                Type tResponse = queryInterface.GetGenericArguments()[0];
                DecorateIfRegistered(services, typeof(IQueryHandler<,>).MakeGenericType(type, tResponse),
                    queryDecorator.MakeGenericType(type, tResponse));
            }
        }

        return services;
    }

    /// <summary>Same tolerance as <c>CrmScopeRegistration</c>'s: skip a type with no handler yet.</summary>
    private static void DecorateIfRegistered(IServiceCollection services, Type serviceType, Type decoratorType)
    {
        if (services.Any(d => d.ServiceType == serviceType))
        {
            services.Decorate(serviceType, decoratorType);
        }
    }
}
