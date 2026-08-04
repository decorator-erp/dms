using BuildingBlocks.DomainEvents;
using BuildingBlocks.Events;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Outbox;

/// <summary>
/// Wraps one module's <see cref="IIntegrationEventHandler{T}"/> so the (event, handler) de-dupe row
/// commits atomically with the handler's own work AND any domain-event cascade the handler's write
/// triggers — never as a separate write through <see cref="SharedDbContext"/>, which a crash between the
/// two commits could leave inconsistent (the handler's effect done, but nothing recorded to stop a retry
/// from redoing it).
///
/// Contract this relies on: <paramref name="innerHandler"/> stages its changes on
/// <typeparamref name="TContext"/> (or calls out to another module's contract) but does NOT call its
/// own <c>SaveChangesAsync</c>.
///
/// <para>
/// <see cref="DomainEventDispatcher{TContext}"/> is invoked here — not via a second decorator layered
/// between this one and the inner handler, which an earlier draft of this fix used and which was wrong:
/// <see cref="DomainEventDispatcher{TContext}.DispatchAsync"/> calls <c>SaveChangesAsync</c> itself,
/// inside its own drain loop, once per iteration — up to 10 times for one cascade. It does not merely
/// stage. A second-decorator design would have produced up to 11 separate commits for one event
/// delivery; a crash between any two of them would leave the cascade partially applied with the inbox
/// row still absent — reintroducing, one layer downstream, the exact bug class this decorator exists to
/// prevent. Wrapping the whole sequence (handler → cascade → inbox row) in one explicit transaction
/// sidesteps that: it does not matter how many internal saves the dispatcher performs, because none of
/// them are visible outside this transaction until <see cref="Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction.CommitAsync"/>
/// runs. A crash or exception anywhere in between rolls back everything — the handler's write, every
/// cascade step, and the inbox row alike — so a retry reruns the whole chain from zero, which is exactly
/// R1's original guarantee, now correctly covering the cascade too.
/// </para>
/// <para>
/// If retry-on-failure is ever enabled on <typeparamref name="TContext"/>'s <c>UseNpgsql(...)</c> call,
/// this method must be wrapped in <c>dbContext.Database.CreateExecutionStrategy().ExecuteAsync(...)</c>
/// or it will throw on the first retry attempt — EF Core requires an execution strategy around any
/// user-initiated transaction once retry-on-failure is configured. Not needed today: no module's
/// <c>UseNpgsql(...)</c> call enables it (confirmed by inspection, not assumed).
/// </para>
/// </summary>
internal sealed class InboxDedupeDecorator<TContext, TEvent>(
    IIntegrationEventHandler<TEvent> innerHandler,
    DomainEventDispatcher<TContext> dispatcher,
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

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        await innerHandler.HandleAsync(@event, ct);
        await dispatcher.DispatchAsync(ct);

        dbContext.Set<InboxMessage>().Add(new InboxMessage(@event.EventId, handlerName, DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);
    }
}
