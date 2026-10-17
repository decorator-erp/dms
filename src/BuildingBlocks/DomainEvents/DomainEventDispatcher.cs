using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace BuildingBlocks.DomainEvents;

/// <summary>
/// Drains domain events from <typeparamref name="TContext"/>'s ChangeTracker and the scoped
/// <see cref="IDomainEventQueue"/>, then invokes their handlers. Called at the unit-of-work /
/// transaction boundary so events raised by set-based writes (ExecuteUpdate/ExecuteDelete) fire even
/// though they bypass the change tracker.
///
/// Generic over the module's own <see cref="DbContext"/> so each module gets its own dispatcher
/// instance wired to its own context — see the remarks on
/// <see cref="ModuleCqrsRegistration.AddModuleCqrs{TContext}"/> for why a single shared,
/// non-generic dispatcher would silently cross module boundaries.
/// </summary>
public sealed class DomainEventDispatcher<TContext>(
    TContext dbContext,
    IDomainEventQueue eventQueue,
    IServiceProvider serviceProvider,
    ILogger<DomainEventDispatcher<TContext>> logger) : IDomainEventDispatcher
    where TContext : DbContext
{
    private const int MaxIterations = 10;

    public async Task DispatchAsync(CancellationToken ct = default)
    {
        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            // Events come from two sources: aggregates tracked by EF (entity.AddDomainEvent) and the
            // scoped queue (set-based operations that don't load an aggregate instance).
            var entitiesWithEvents = dbContext.ChangeTracker
                .Entries<Entity>()
                .Where(e => e.Entity.DomainEvents.Count > 0)
                .Select(e => e.Entity)
                .ToList();

            IReadOnlyList<IDomainEvent> queuedEvents = eventQueue.DrainAll();

            if (entitiesWithEvents.Count == 0 && queuedEvents.Count == 0)
            {
                return;
            }

            var domainEvents = entitiesWithEvents
                .SelectMany(e => e.DomainEvents)
                .Concat(queuedEvents)
                .ToList();

            foreach (Entity? entity in entitiesWithEvents)
            {
                entity.ClearDomainEvents();
            }

            foreach (IDomainEvent? domainEvent in domainEvents)
            {
                Type handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());

                // Invoke via the PUBLIC interface method, not `dynamic`: handlers commonly live as
                // `internal` types in a module's Application assembly, and the dynamic binder (running
                // from BuildingBlocks) can't see members on types it has no access to. Reflecting the
                // interface method side-steps accessibility entirely.
                MethodInfo handleMethod = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.Handle))!;
                IEnumerable<object?> handlers = serviceProvider.GetServices(handlerType);

                foreach (object? handler in handlers)
                {
                    if (handler is null)
                    {
                        continue;
                    }

                    try
                    {
                        await (Task)handleMethod.Invoke(handler, [domainEvent, ct])!;
                    }
                    catch (TargetInvocationException ex) when (ex.InnerException is not null)
                    {
                        // Unwrap so callers see the real exception (e.g. for transaction rollback), not the reflection wrapper.
                        ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                    }
                }
            }

            // Handlers may resolve gates purely set-based (no tracked changes); skip the empty save.
            if (dbContext.ChangeTracker.HasChanges())
            {
                await dbContext.SaveChangesAsync(ct);
            }
        }

        logger.LogWarning(
            "Domain event dispatch exceeded {MaxIterations} iterations; stopping. Likely a cascade cycle.",
            MaxIterations);
    }
}
