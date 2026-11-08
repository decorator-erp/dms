using System.Reflection;
using BuildingBlocks.Messaging;
using Documents.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Documents.Infrastructure.Decorators;

/// <summary>
/// Mirrors <c>Sites.Infrastructure.Decorators.SiteScopeRegistration</c>: scans the CONTRACTS assembly, where the
/// <see cref="IDocumentsScoped"/> messages live, and is called from <c>DocumentsModule.Register</c> AFTER
/// <c>AddModuleCqrs&lt;DocumentsDbContext&gt;</c>. Scrutor's <c>Decorate</c> wraps in call order, so the scope
/// check is added last to end up outermost — a scope check that ran after validation would report on a payload
/// the caller was never entitled to have validated. <c>DocumentsScopeDecoratorWiringTests</c> proves the result.
/// </summary>
internal static class DocumentsScopeRegistration
{
    public static IServiceCollection AddDocumentsScopeDecorator(this IServiceCollection services, Assembly assembly)
    {
        IEnumerable<Type> scopedTypes = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IDocumentsScoped).IsAssignableFrom(t));

        foreach (Type type in scopedTypes)
        {
            if (typeof(ICommand).IsAssignableFrom(type))
            {
                DecorateIfRegistered(services, typeof(ICommandHandler<>).MakeGenericType(type),
                    typeof(DocumentsScopeDecorator.CommandHandler<>).MakeGenericType(type));
                continue;
            }

            Type? commandInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
            if (commandInterface is not null)
            {
                Type tResponse = commandInterface.GetGenericArguments()[0];
                DecorateIfRegistered(services, typeof(ICommandHandler<,>).MakeGenericType(type, tResponse),
                    typeof(DocumentsScopeDecorator.CommandHandler<,>).MakeGenericType(type, tResponse));
                continue;
            }

            Type? queryInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));
            if (queryInterface is not null)
            {
                Type tResponse = queryInterface.GetGenericArguments()[0];
                DecorateIfRegistered(services, typeof(IQueryHandler<,>).MakeGenericType(type, tResponse),
                    typeof(DocumentsScopeDecorator.QueryHandler<,>).MakeGenericType(type, tResponse));
            }
        }

        return services;
    }

    /// <summary>Scrutor's <c>Decorate</c> throws when the target isn't registered; the wiring test is what
    /// catches a scoped message whose handler is missing, so this tolerates it rather than failing boot.</summary>
    private static void DecorateIfRegistered(IServiceCollection services, Type serviceType, Type decoratorType)
    {
        if (services.Any(d => d.ServiceType == serviceType))
        {
            services.Decorate(serviceType, decoratorType);
        }
    }
}
