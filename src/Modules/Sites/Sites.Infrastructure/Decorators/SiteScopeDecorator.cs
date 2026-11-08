using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;
using Sites.Contracts;
using Sites.Contracts.Surveys;
using Sites.Domain;
using Sites.Infrastructure.Persistence;

namespace Sites.Infrastructure.Decorators;

/// <summary>
/// The original of a pattern this repo already copied twice. <c>ProjectScopeDecorator</c> (G5-07) and
/// <c>CrmScopeDecorator</c> (G9-08) are both descendants of the backend's <c>SiteScopeDecorator</c>;
/// Sites gets it last, and mirrors the two existing implementations rather than the backend directly.
///
/// <para><b>A site is not its own access boundary — its project is.</b> Every
/// <see cref="ISiteScoped"/> message carries a <c>SiteId</c>, so this resolves that site to its
/// <c>ProjectId</c> and defers to <see cref="IProjectAccessGuard"/>. One extra indexed
/// <c>SELECT "ProjectId"</c> per scoped request, which is the price of not duplicating Projects'
/// membership rules inside Sites.</para>
///
/// <para><b>Everything collapses to 404</b> — the <c>ProjectScopeDecorator</c> shape, not the
/// <c>CrmScopeDecorator</c> one (which propagates the guard's own 403 because "you have no CRM
/// identity" is a fact about the caller, not about a row). Three distinct cases become one answer here:
/// the id is nonsense, the site was soft-deleted, or it belongs to a project the caller cannot see. A
/// caller who cannot see the project must not be able to tell those apart, or the 404 becomes an
/// existence oracle. <c>CLAUDE.md</c>'s authorization order puts the module scope decorator at 404.</para>
///
/// <para>The query runs through <see cref="SitesDbContext.Sites"/>, which carries the
/// <c>IsActive</c> filter — so a soft-deleted survey resolves to nothing and takes the 404 branch
/// without a special case.</para>
///
/// <para>P1-A: both checks name the <see cref="ProjectAccessArea.Sites"/> area. A survey has no stage, so a project
/// access grant reaches it only when it covers the whole project (stage null); writes need the grant's level.</para>
/// </summary>
internal static class SiteScopeDecorator
{
    private static async Task<Result> CheckAsync(
        ISiteScoped scoped, SitesDbContext db, IProjectAccessGuard guard, CancellationToken ct)
    {
        Guid? projectId = await db.Sites
            .AsNoTracking()
            .Where(s => s.Id == scoped.SiteId)
            .Select(s => (Guid?)s.ProjectId)
            .FirstOrDefaultAsync(ct);

        if (projectId is not { } project)
        {
            return SiteErrors.NotFound;
        }

        Result access = await guard.EnsureProjectAccessAsync(project, scoped.AppUserId, ProjectAccessArea.Sites, null, ct);
        return access.IsFailure ? SiteErrors.NotFound : Result.Success();
    }

    /// <summary>C6: a write needs scope (404), then the level <see cref="SiteProjectLevels"/> names for it (403
    /// <see cref="SiteErrors.InsufficientLevel"/>), then a project that takes work (409
    /// <see cref="SiteErrors.ProjectClosed"/>: <see cref="IsClosed"/>, as B-S-M7 refuses a new survey). R1-7: the
    /// decision on a submitted survey is the one write a closed project still takes, so a survey submitted before the
    /// project closed is resolved rather than stuck; capture, reopen and delete stay refused.
    /// The level reader's sources are the guard's scope, so no level is the same 404.</summary>
    private static async Task<Result> CheckWriteAsync(
        ISiteScoped scoped, SitesDbContext db, IProjectAccessLevelReader levels, IProjectDirectory projects,
        CancellationToken ct)
    {
        Guid? projectId = await db.Sites
            .AsNoTracking()
            .Where(s => s.Id == scoped.SiteId)
            .Select(s => (Guid?)s.ProjectId)
            .FirstOrDefaultAsync(ct);

        if (projectId is not { } project)
        {
            return SiteErrors.NotFound;
        }

        ProjectMemberAccess? level = await levels.GetLevelAsync(project, scoped.AppUserId, ProjectAccessArea.Sites, null, ct);
        if (level is null)
        {
            return SiteErrors.NotFound;
        }

        if (level.Value < SiteProjectLevels.Required[scoped.GetType()])
        {
            return SiteErrors.InsufficientLevel;
        }

        ProjectSummary? summary = await projects.GetProjectAsync(project, ct);
        if (summary is null)
        {
            return SiteErrors.NotFound;
        }

        return IsClosed(summary) && scoped is not DecideSiteCommand ? SiteErrors.ProjectClosed : Result.Success();
    }

    /// <summary>B-S-M7 / C6 / R1-7: the same "closed" as Projects' own <c>ProjectLiveness</c> — a deactivated, on-hold,
    /// Completed or Cancelled project takes no survey work (only Draft and InProgress do).</summary>
    internal static bool IsClosed(ProjectSummary project) =>
        !project.IsActive || project.Status is not (ProjectStatus.Draft or ProjectStatus.InProgress);

    internal sealed class CommandHandler<TCommand>(
        ICommandHandler<TCommand> innerHandler, SitesDbContext db, IProjectAccessLevelReader levels,
        IProjectDirectory projects)
        : ICommandHandler<TCommand>
        where TCommand : ICommand, ISiteScoped
    {
        public async Task<Result> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            Result check = await CheckWriteAsync(command, db, levels, projects, ct);
            return check.IsFailure ? check : await innerHandler.HandleAsync(command, ct);
        }
    }

    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> innerHandler, SitesDbContext db, IProjectAccessLevelReader levels,
        IProjectDirectory projects)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>, ISiteScoped
    {
        public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            Result check = await CheckWriteAsync(command, db, levels, projects, ct);
            return check.IsFailure
                ? Result.Failure<TResponse>(check.Error)
                : await innerHandler.HandleAsync(command, ct);
        }
    }

    internal sealed class QueryHandler<TQuery, TResponse>(
        IQueryHandler<TQuery, TResponse> innerHandler, SitesDbContext db, IProjectAccessGuard guard)
        : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>, ISiteScoped
    {
        public async Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken ct = default)
        {
            Result check = await CheckAsync(query, db, guard, ct);
            return check.IsFailure
                ? Result.Failure<TResponse>(check.Error)
                : await innerHandler.HandleAsync(query, ct);
        }
    }
}
