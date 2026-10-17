using System.Reflection;
using BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;

namespace Projects.Infrastructure.Decorators;

/// <summary>
/// Called from <c>ProjectsModule.Register</c> AFTER <c>AddModuleCqrs&lt;ProjectsDbContext&gt;</c>, so
/// <see cref="ProjectScopeDecorator"/> wraps outermost — Scrutor's <c>Decorate</c> wraps in call order,
/// and <c>AddModuleCqrs</c> already decorated each closed handler type with
/// Validation→Logging→DomainEventDispatch. Reflects <see cref="IProjectScoped"/> COMMAND/QUERY types
/// (not handler types, unlike <c>ModuleCqrsRegistration</c>'s own scan) because the scope decorator only
/// needs to know a type's own generic shape (<c>ICommand</c>/<c>ICommand&lt;TResponse&gt;</c>/
/// <c>IQuery&lt;TResponse&gt;</c>) to name the handler service type it wraps. Scans <c>Projects.
/// Contracts</c> — every <see cref="IProjectScoped"/> command/query record is declared THERE, not in
/// <c>Projects.Application</c> (which only holds their handlers); scanning the wrong assembly here once
/// found zero matches and silently skipped every decoration (caught by
/// <c>ProjectFlowTests</c>' scope-decorator test).
/// </summary>
internal static class ProjectScopeRegistration
{
    public static IServiceCollection AddProjectScopeDecorator(this IServiceCollection services, Assembly assembly)
    {
        IEnumerable<Type> scopedTypes = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IProjectScoped).IsAssignableFrom(t));

        foreach (Type? type in scopedTypes)
        {
            if (typeof(ICommand).IsAssignableFrom(type))
            {
                DecorateIfRegistered(services, typeof(ICommandHandler<>).MakeGenericType(type),
                    typeof(ProjectScopeDecorator.CommandHandler<>).MakeGenericType(type));
                continue;
            }

            Type? commandInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
            if (commandInterface is not null)
            {
                Type tResponse = commandInterface.GetGenericArguments()[0];
                DecorateIfRegistered(services, typeof(ICommandHandler<,>).MakeGenericType(type, tResponse),
                    typeof(ProjectScopeDecorator.CommandHandler<,>).MakeGenericType(type, tResponse));
                continue;
            }

            Type? queryInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));
            if (queryInterface is not null)
            {
                Type tResponse = queryInterface.GetGenericArguments()[0];
                DecorateIfRegistered(services, typeof(IQueryHandler<,>).MakeGenericType(type, tResponse),
                    typeof(ProjectScopeDecorator.QueryHandler<,>).MakeGenericType(type, tResponse));
            }
        }

        return services;
    }

    /// <summary>Some <see cref="IProjectScoped"/> types (the gate commands, G5-08) don't have a handler
    /// built yet at G5-07 — Scrutor's <c>Decorate</c> throws if the target service isn't registered, so
    /// this skips those rather than failing module registration for a feature this task doesn't build.
    /// Every type WITH a handler still gets decorated normally.</summary>
    private static void DecorateIfRegistered(IServiceCollection services, Type serviceType, Type decoratorType)
    {
        if (services.Any(d => d.ServiceType == serviceType))
        {
            services.Decorate(serviceType, decoratorType);
        }
    }
}
