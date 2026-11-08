using BuildingBlocks.Messaging;
using Projects.Contracts;
using Projects.Infrastructure.Persistence;
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
/// <para>P10: a command (never a query) then needs its <see cref="IRequiresProjectLevel"/>; the caller's level comes
/// from <see cref="IProjectAccessLevelReader"/>, whose sources are the guard's scope, so one statement answers
/// both. The generic constraint makes a scoped command without a declared level fail at registration.</para>
/// <para>P1-A: which project access grants count is <see cref="ProjectScopeAreas"/>' decision: the area and stage a
/// message touches, or none (membership only).</para>
/// </summary>
internal static class ProjectScopeDecorator
{
    /// <summary>P10 (C5): scope first — no level at all is 404 — then the command's declared level — below it is
    /// 403 <see cref="Domain.ProjectErrors.InsufficientLevel"/>. Both before the handler, so before any 409. P1-A: the
    /// level counts a grant only when <see cref="ProjectScopeAreas"/> names an area for the command.</summary>
    private static async Task<Result> CheckLevelAsync<TCommand>(
        TCommand command, IProjectAccessLevelReader levels, ProjectsDbContext db, CancellationToken ct)
        where TCommand : IProjectScoped, IRequiresProjectLevel
    {
        ProjectScopeAreas.Target target = await ProjectScopeAreas.ResolveAsync(command, isWrite: true, db, ct);
        ProjectMemberAccess? level = target.Kind == ProjectScopeAreas.Kind.Stage
            ? await levels.GetLevelAsync(command.ProjectId, command.UserId, target.Area, target.StageId, ct)
            : await levels.GetLevelAsync(command.ProjectId, command.UserId, ct);
        if (level is null)
        {
            return Domain.ProjectErrors.NotFound;
        }

        return level.Value < command.RequiredProjectLevel ? Domain.ProjectErrors.InsufficientLevel : Result.Success();
    }

    /// <summary>P1-A: a query's scope by its <see cref="ProjectScopeAreas.Target"/>. Any failure is 404.</summary>
    private static async Task<bool> CanReadAsync<TQuery>(
        TQuery query, IProjectAccessGuard guard, IProjectAccessLevelReader levels, ProjectsDbContext db, CancellationToken ct)
        where TQuery : IProjectScoped
    {
        ProjectScopeAreas.Target target = await ProjectScopeAreas.ResolveAsync(query, isWrite: false, db, ct);
        return target.Kind switch
        {
            ProjectScopeAreas.Kind.AnyGrant =>
                (await guard.EnsureProjectAccessAsync(query.ProjectId, query.UserId, ct)).IsSuccess,
            ProjectScopeAreas.Kind.Stage =>
                (await guard.EnsureProjectAccessAsync(query.ProjectId, query.UserId, target.Area, target.StageId, ct)).IsSuccess,
            ProjectScopeAreas.Kind.List =>
                await guard.GetStageFilterAsync(query.ProjectId, query.UserId, target.Area, ct) is not { Count: 0 },
            _ => await levels.GetLevelAsync(query.ProjectId, query.UserId, ct) is not null,
        };
    }

    internal sealed class CommandHandler<TCommand>(
        ICommandHandler<TCommand> innerHandler, IProjectAccessLevelReader levels, ProjectsDbContext db)
        : ICommandHandler<TCommand>
        where TCommand : ICommand, IProjectScoped, IRequiresProjectLevel
    {
        public async Task<Result> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            Result access = await CheckLevelAsync(command, levels, db, ct);
            return access.IsFailure ? access : await innerHandler.HandleAsync(command, ct);
        }
    }

    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> innerHandler, IProjectAccessLevelReader levels, ProjectsDbContext db)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>, IProjectScoped, IRequiresProjectLevel
    {
        public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            Result access = await CheckLevelAsync(command, levels, db, ct);
            return access.IsFailure
                ? Result.Failure<TResponse>(access.Error)
                : await innerHandler.HandleAsync(command, ct);
        }
    }

    internal sealed class QueryHandler<TQuery, TResponse>(
        IQueryHandler<TQuery, TResponse> innerHandler, IProjectAccessGuard guard, IProjectAccessLevelReader levels,
        ProjectsDbContext db)
        : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>, IProjectScoped
    {
        public async Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken ct = default) =>
            await CanReadAsync(query, guard, levels, db, ct)
                ? await innerHandler.HandleAsync(query, ct)
                : Result.Failure<TResponse>(Domain.ProjectErrors.NotFound);
    }
}
