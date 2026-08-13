using BuildingBlocks.DomainEvents;
using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace BuildingBlocks.Behaviors;

/// <summary>
/// Dispatches queued domain events after a command succeeds, so events fire regardless of whether the
/// handler used an explicit transaction or SaveChanges. Idempotent: handlers that already dispatched
/// (via a transaction commit or SaveChanges) leave an empty queue, making this a cheap no-op.
///
/// Generic over the module's own <see cref="DbContext"/> so each module's pipeline dispatches through
/// its own <see cref="DomainEventDispatcher{TContext}"/> instance. See
/// <see cref="ModuleCqrsRegistration.AddModuleCqrs{TContext}"/> — the registration deliberately wires
/// this per closed (TCommand, TResponse) service type rather than via Scrutor's open-generic Decorate,
/// which would rewrap every other module's already-decorated handlers with the wrong module's
/// dispatcher the moment a second module registers.
///
/// <para>
/// Wraps the inner handler call AND the dispatch loop in one explicit transaction — the command-path
/// counterpart to <c>InboxDedupeDecorator</c>'s own F1 fix (Phase 2, decision #10). Flagged as a deferred
/// TODO at G5-02 (no cascade existed yet to prove it against) and fixed here at G5-08, whose own
/// gate-cascade verify ("a 3-level gate cascade resolves in one command's transaction") depends on it.
/// Without the wrap, the inner handler's own <c>SaveChangesAsync</c> and <see cref="DomainEventDispatcher{TContext}.DispatchAsync"/>'s
/// internal per-iteration saves (up to 10) are separate commits — a crash between any two of them leaves
/// the direct write committed with its cascade unresolved. Harmless for every command with no cascade:
/// one extra `BEGIN`/`COMMIT` around what was already a single atomic save.
/// </para>
/// </summary>
internal static class DomainEventDispatchDecorator<TContext> where TContext : DbContext
{
    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> innerHandler,
        DomainEventDispatcher<TContext> dispatcher,
        TContext dbContext,
        OutboxSignal signal)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

            Result<TResponse> result = await innerHandler.HandleAsync(command, ct);

            if (result.IsSuccess)
            {
                await dispatcher.DispatchAsync(ct);
                await transaction.CommitAsync(ct);

                // After commit, never before - see InboxDedupeDecorator's identical nudge for why.
                // Most commands enqueue nothing, so this is a cheap no-op call most of the time; the
                // ones that do call db.Outbox().Enqueue(...) are what this exists for.
                signal.Nudge();
            }
            else
            {
                await transaction.RollbackAsync(ct);
            }

            return result;
        }
    }

    internal sealed class CommandHandler<TCommand>(
        ICommandHandler<TCommand> innerHandler,
        DomainEventDispatcher<TContext> dispatcher,
        TContext dbContext,
        OutboxSignal signal)
        : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public async Task<Result> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

            Result result = await innerHandler.HandleAsync(command, ct);

            if (result.IsSuccess)
            {
                await dispatcher.DispatchAsync(ct);
                await transaction.CommitAsync(ct);

                // After commit, never before - see InboxDedupeDecorator's identical nudge for why.
                // Most commands enqueue nothing, so this is a cheap no-op call most of the time; the
                // ones that do call db.Outbox().Enqueue(...) are what this exists for.
                signal.Nudge();
            }
            else
            {
                await transaction.RollbackAsync(ct);
            }

            return result;
        }
    }
}
