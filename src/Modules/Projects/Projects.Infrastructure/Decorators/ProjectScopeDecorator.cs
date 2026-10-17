using BuildingBlocks.Messaging;
using Projects.Contracts;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Infrastructure.Decorators;

/// <summary>
/// Modeled on the backend's <c>SiteScopeDecorator</c>, registered in Projects' OWN pipeline (not the
/// shared <c>ModuleCqrsRegistration</c> — see that class's own remarks on why a module-specific decorator
/// can't live there). Wraps every command/query implementing <see cref="IProjectScoped"/>: resolves
/// access via <see cref="IProjectAccessGuard"/> before the inner handler ever runs. Any failure —
/// project doesn't exist, or the caller just isn't a member — collapses to
/// <see cref="Domain.ProjectErrors.NotFound"/> (404), never <see cref="Domain.ProjectErrors.AccessDenied"/>'s
/// own 403: <c>CLAUDE.md</c>'s authorization order puts the module scope decorator at 404, not 403, so an
/// unauthorized caller learns nothing about whether the project id they guessed is even real.
/// </summary>
internal static class ProjectScopeDecorator
{
    internal sealed class CommandHandler<TCommand>(
        ICommandHandler<TCommand> innerHandler, IProjectAccessGuard guard)
        : ICommandHandler<TCommand>
        where TCommand : ICommand, IProjectScoped
    {
        public async Task<Result> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            Result access = await guard.EnsureProjectAccessAsync(command.ProjectId, command.UserId, ct);
            return access.IsFailure ? Domain.ProjectErrors.NotFound : await innerHandler.HandleAsync(command, ct);
        }
    }

    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> innerHandler, IProjectAccessGuard guard)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>, IProjectScoped
    {
        public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            Result access = await guard.EnsureProjectAccessAsync(command.ProjectId, command.UserId, ct);
            return access.IsFailure
                ? Result.Failure<TResponse>(Domain.ProjectErrors.NotFound)
                : await innerHandler.HandleAsync(command, ct);
        }
    }

    internal sealed class QueryHandler<TQuery, TResponse>(
        IQueryHandler<TQuery, TResponse> innerHandler, IProjectAccessGuard guard)
        : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>, IProjectScoped
    {
        public async Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken ct = default)
        {
            Result access = await guard.EnsureProjectAccessAsync(query.ProjectId, query.UserId, ct);
            return access.IsFailure
                ? Result.Failure<TResponse>(Domain.ProjectErrors.NotFound)
                : await innerHandler.HandleAsync(query, ct);
        }
    }
}
