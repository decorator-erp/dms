using BuildingBlocks.DomainEvents;
using BuildingBlocks.Messaging;
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
/// </summary>
internal static class DomainEventDispatchDecorator<TContext> where TContext : DbContext
{
    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> innerHandler,
        DomainEventDispatcher<TContext> dispatcher)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            Result<TResponse> result = await innerHandler.HandleAsync(command, ct);

            if (result.IsSuccess)
                await dispatcher.DispatchAsync(ct);

            return result;
        }
    }

    internal sealed class CommandHandler<TCommand>(
        ICommandHandler<TCommand> innerHandler,
        DomainEventDispatcher<TContext> dispatcher)
        : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public async Task<Result> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            Result result = await innerHandler.HandleAsync(command, ct);

            if (result.IsSuccess)
                await dispatcher.DispatchAsync(ct);

            return result;
        }
    }
}
