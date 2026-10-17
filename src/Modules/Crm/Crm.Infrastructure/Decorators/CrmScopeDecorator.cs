using BuildingBlocks.Messaging;
using Crm.Contracts;
using SharedKernel;

namespace Crm.Infrastructure.Decorators;

/// <summary>
/// Mirrors <c>Projects.Infrastructure.Decorators.ProjectScopeDecorator</c>. Wraps every command/query
/// implementing <see cref="ICrmScoped"/>: resolves the caller via <see cref="ICrmAccessGuard"/> before
/// the inner handler ever runs. Required by <c>CLAUDE.md</c>'s authorization order (route claims → 403;
/// module scope decorator → 404; aggregate state → 409) — <see cref="ICrmAccessGuard.ResolveActorAsync"/>'s
/// own failure is a 403-shaped "no CRM identity", not a 404, because it fires before this decorator
/// even knows which aggregate the caller is asking about; a genuinely mis-scoped resource (asking about
/// someone else's project) is the inner handler's own 404, resolved through the actor this decorator
/// hands it.
/// </summary>
internal static class CrmScopeDecorator
{
    internal sealed class CommandHandler<TCommand>(
        ICommandHandler<TCommand> innerHandler, ICrmAccessGuard guard)
        : ICommandHandler<TCommand>
        where TCommand : ICommand, ICrmScoped
    {
        public async Task<Result> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            Result<CrmActor> actor = await guard.ResolveActorAsync(command.AppUserId, ct);
            return actor.IsFailure ? Result.Failure(actor.Error) : await innerHandler.HandleAsync(command, ct);
        }
    }

    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> innerHandler, ICrmAccessGuard guard)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>, ICrmScoped
    {
        public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            Result<CrmActor> actor = await guard.ResolveActorAsync(command.AppUserId, ct);
            return actor.IsFailure
                ? Result.Failure<TResponse>(actor.Error)
                : await innerHandler.HandleAsync(command, ct);
        }
    }

    internal sealed class QueryHandler<TQuery, TResponse>(
        IQueryHandler<TQuery, TResponse> innerHandler, ICrmAccessGuard guard)
        : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>, ICrmScoped
    {
        public async Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken ct = default)
        {
            Result<CrmActor> actor = await guard.ResolveActorAsync(query.AppUserId, ct);
            return actor.IsFailure
                ? Result.Failure<TResponse>(actor.Error)
                : await innerHandler.HandleAsync(query, ct);
        }
    }
}
