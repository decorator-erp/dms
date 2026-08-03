using BuildingBlocks.Events;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Outbox;

public interface IOutboxWriter
{
    void Enqueue(IIntegrationEvent @event);
}

public static class DbContextOutboxExtensions
{
    /// <summary>
    /// Stages <paramref name="event"/> on <paramref name="dbContext"/>'s own change tracker — it
    /// commits together with whatever else the caller adds when it calls <c>SaveChangesAsync</c>.
    ///
    /// Deliberately an extension method, not a DI-injected service: a shared <c>IOutboxWriter</c>
    /// registration would collide the same way a shared, non-generic domain-event dispatcher would
    /// (see the remarks on <c>BuildingBlocks.ModuleCqrsRegistration</c>) — the whole point is that this
    /// always writes through the CALLING module's own context, and an extension on the base
    /// <see cref="DbContext"/> type gives every module that for free, with no per-module registration.
    /// </summary>
    public static IOutboxWriter Outbox(this DbContext dbContext) => new OutboxWriter(dbContext);

    private sealed class OutboxWriter(DbContext dbContext) : IOutboxWriter
    {
        public void Enqueue(IIntegrationEvent @event) =>
            dbContext.Set<OutboxMessage>().Add(OutboxMessage.From(@event, @event.GetType()));
    }
}
