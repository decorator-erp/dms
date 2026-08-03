using BuildingBlocks.Events;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Outbox;

/// <summary>
/// Wraps one module's <see cref="IIntegrationEventHandler{T}"/> so the (event, handler) de-dupe row
/// commits in the SAME <c>SaveChangesAsync</c> call as the handler's own work — never as a separate
/// write through <see cref="SharedDbContext"/>, which a crash between the two commits could leave
/// inconsistent (the handler's effect done, but nothing recorded to stop a retry from redoing it).
///
/// Contract this relies on: <paramref name="innerHandler"/> stages its changes on
/// <typeparamref name="TContext"/> (or calls out to another module's contract) but does NOT call its
/// own <c>SaveChangesAsync</c> — this decorator's single save at the end is the one commit point.
/// </summary>
internal sealed class InboxDedupeDecorator<TContext, TEvent>(
    IIntegrationEventHandler<TEvent> innerHandler,
    TContext dbContext)
    : IIntegrationEventHandler<TEvent>
    where TContext : DbContext
    where TEvent : IIntegrationEvent
{
    public async Task HandleAsync(TEvent @event, CancellationToken ct)
    {
        var handlerName = innerHandler.GetType().FullName ?? innerHandler.GetType().Name;

        var alreadyProcessed = await dbContext.Set<InboxMessage>()
            .AnyAsync(m => m.EventId == @event.EventId && m.HandlerName == handlerName, ct);

        if (alreadyProcessed)
            return;

        await innerHandler.HandleAsync(@event, ct);

        dbContext.Set<InboxMessage>().Add(new InboxMessage(@event.EventId, handlerName, DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync(ct);
    }
}
