using System.Reflection;
using BuildingBlocks.Behaviors;
using BuildingBlocks.DomainEvents;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel;

namespace BuildingBlocks;

/// <summary>
/// Registers one module's CQRS pipeline: handler implementations found in <paramref name="assembly"/>,
/// wrapped in Validation → Logging → DomainEventDispatch, in that order (innermost to outermost) —
/// so a failing validator short-circuits before the handler runs, and domain events dispatch only
/// after the whole chain, including logging, has reported success.
///
/// <para>
/// This deliberately does NOT use Scrutor's open-generic <c>Decorate(typeof(ICommandHandler&lt;,&gt;), ...)</c>
/// the way the ported single-assembly backend did. Scrutor's open-generic decoration rewraps every
/// currently-registered closed generic matching that pattern — across the WHOLE service collection, not
/// just the assembly being scanned. In a composition root where every module calls this method in turn,
/// a second module's call would re-wrap the first module's already-decorated handlers a second time,
/// with the wrong module's <see cref="DomainEventDispatcher{TContext}"/> — silently constructing that
/// module's <typeparamref name="TContext"/> for requests that never touch it, and defeating the "handler
/// registration never spans modules" guarantee this method exists to provide.
/// </para>
/// <para>
/// Instead, each handler's fully-closed service type (e.g. <c>ICommandHandler&lt;LoginCommand, AuthResponse&gt;</c>)
/// is decorated individually, discovered by reflecting <paramref name="assembly"/> — the same assembly
/// Scrutor scanned. Because two modules never declare the same command/query type, their closed service
/// types never collide, so decorating per closed type is inherently isolated without needing to track
/// "already decorated" state.
/// </para>
/// </summary>
public static class ModuleCqrsRegistration
{
    public static IServiceCollection AddModuleCqrs<TContext>(this IServiceCollection services, Assembly assembly)
        where TContext : DbContext
    {
        services.TryAddScoped<IDomainEventQueue, DomainEventQueue>();
        services.AddScoped<DomainEventDispatcher<TContext>>();

        services.Scan(scan => scan.FromAssemblies(assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<>)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo(typeof(IDomainEventHandler<>)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime());

        foreach (var implementationType in assembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false }))
        {
            foreach (var handlerInterface in implementationType.GetInterfaces().Where(i => i.IsGenericType))
            {
                var definition = handlerInterface.GetGenericTypeDefinition();
                var genericArgs = handlerInterface.GetGenericArguments();

                if (definition == typeof(ICommandHandler<>))
                {
                    DecorateCommandHandlerNoResponse<TContext>(services, genericArgs[0]);
                }
                else if (definition == typeof(ICommandHandler<,>))
                {
                    DecorateCommandHandlerWithResponse<TContext>(services, genericArgs[0], genericArgs[1]);
                }
                else if (definition == typeof(IQueryHandler<,>))
                {
                    DecorateQueryHandler(services, genericArgs[0], genericArgs[1]);
                }
            }
        }

        return services;
    }

    private static void DecorateCommandHandlerNoResponse<TContext>(IServiceCollection services, Type tCommand)
        where TContext : DbContext
    {
        var serviceType = typeof(ICommandHandler<>).MakeGenericType(tCommand);

        services.Decorate(serviceType, typeof(ValidationDecorator.CommandHandler<>).MakeGenericType(tCommand));
        services.Decorate(serviceType, typeof(LoggingDecorator.CommandHandler<>).MakeGenericType(tCommand));
        services.Decorate(
            serviceType,
            typeof(DomainEventDispatchDecorator<>.CommandHandler<>).MakeGenericType(typeof(TContext), tCommand));
    }

    private static void DecorateCommandHandlerWithResponse<TContext>(
        IServiceCollection services, Type tCommand, Type tResponse)
        where TContext : DbContext
    {
        var serviceType = typeof(ICommandHandler<,>).MakeGenericType(tCommand, tResponse);

        services.Decorate(serviceType, typeof(ValidationDecorator.CommandHandler<,>).MakeGenericType(tCommand, tResponse));
        services.Decorate(serviceType, typeof(LoggingDecorator.CommandHandler<,>).MakeGenericType(tCommand, tResponse));
        services.Decorate(
            serviceType,
            typeof(DomainEventDispatchDecorator<>.CommandHandler<,>).MakeGenericType(typeof(TContext), tCommand, tResponse));
    }

    private static void DecorateQueryHandler(IServiceCollection services, Type tQuery, Type tResponse)
    {
        var serviceType = typeof(IQueryHandler<,>).MakeGenericType(tQuery, tResponse);

        services.Decorate(serviceType, typeof(LoggingDecorator.QueryHandler<,>).MakeGenericType(tQuery, tResponse));
    }
}
