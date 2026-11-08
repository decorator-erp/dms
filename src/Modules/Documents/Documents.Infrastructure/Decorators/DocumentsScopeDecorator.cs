using BuildingBlocks;
using BuildingBlocks.Messaging;
using Documents.Contracts;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Documents.Infrastructure.Decorators;

/// <summary>
/// BF-1 prerequisite: Documents' project scope — the descendant of <c>SiteScopeDecorator</c> and
/// <c>CrmProjectScopeDecorator</c>. Until this, every Documents route checked only its claim, so a holder of
/// <c>documents.read</c> could list and download any project's files.
///
/// <para><b>A document is not its own access boundary — its project is.</b> Every <see cref="IDocumentsScoped"/>
/// message resolves to one project: directly, through <see cref="IProjectDirectory"/> for a stage or a task, or
/// from this module's own folder and document rows. Then:</para>
/// <list type="bullet">
/// <item><b>Reads</b> ask <see cref="IProjectAccessGuard"/> (bypass: <c>project.read_all</c>).</item>
/// <item><b>Writes</b> (every command) ask <see cref="IProjectAccessLevelReader"/>: no level is the guard's
/// own "no access" (its sources are the guard's scope), and a level below Editor is 403
/// <see cref="ProjectDocumentErrors.InsufficientLevel"/> — a Viewer is read-only here as everywhere else.
/// <c>project.read_all</c> reads as Manager.</item>
/// </list>
///
/// <para><b>Everything that is not a level refusal collapses to 404</b> — an unknown id and a project the caller
/// cannot reach are one answer, or the 404 becomes an existence oracle (CLAUDE.md: route claim 403 → module
/// scope 404 → state 409). Folder messages answer with the folder's own NotFound, the rest with the document's,
/// so the codes match what the handlers already return.</para>
///
/// <para><b>The scope does not filter on <c>IsActive</c>.</b> It answers "which project?", nothing else; whether
/// a soft-deleted row counts is each handler's own rule, and they differ on purpose — the list and get-by-id
/// reads hide it, while a revision upload deliberately resolves a lineage through a deleted middle revision
/// (G7-02 correction 4). A deleted row in a project the caller can reach still gets the handler's 404.</para>
///
/// <para>The caller comes from <see cref="ICurrentUser"/>: several of these messages carry no user id, and
/// nothing outside the HTTP endpoints sends them. No user is a 404.</para>
/// </summary>
internal static class DocumentsScopeDecorator
{
    private static async Task<Result> CheckAsync(
        IDocumentsScoped scoped, bool isWrite, DocumentsDbContext db, IProjectDirectory projects,
        IProjectAccessGuard guard, IProjectAccessLevelReader levels, ICurrentUser currentUser, CancellationToken ct)
    {
        Error notFound = scoped is IDocumentsFolderScoped ? DocumentFolderErrors.NotFound : ProjectDocumentErrors.NotFound;

        Guid? projectId = await ResolveProjectAsync(scoped, db, projects, ct);
        if (projectId is not { } project || string.IsNullOrEmpty(currentUser.UserId))
        {
            return notFound;
        }

        if (!isWrite)
        {
            Result access = await guard.EnsureProjectAccessAsync(project, currentUser.UserId, ct);
            return access.IsFailure ? notFound : Result.Success();
        }

        ProjectMemberAccess? level = await levels.GetLevelAsync(project, currentUser.UserId, ct);
        if (level is null)
        {
            return notFound;
        }

        return level.Value < ProjectMemberAccess.Editor ? ProjectDocumentErrors.InsufficientLevel : Result.Success();
    }

    private static async Task<Guid?> ResolveProjectAsync(
        IDocumentsScoped scoped, DocumentsDbContext db, IProjectDirectory projects, CancellationToken ct) => scoped switch
        {
            IDocumentsProjectScoped p => p.ProjectId,
            IDocumentsStageScoped s => (await projects.GetStageAsync(s.ProjectStageId, ct))?.ProjectId,
            IDocumentsTaskScoped t => (await projects.GetTaskAsync(t.ProjectTaskId, ct))?.ProjectId,
            IDocumentsFolderScoped f => await db.DocumentFolders.AsNoTracking()
                .Where(x => x.Id == f.FolderId)
                .Select(x => (Guid?)x.ProjectId)
                .FirstOrDefaultAsync(ct),
            IDocumentsDocumentScoped d => await db.ProjectDocuments.AsNoTracking()
                .Where(x => x.Id == d.DocumentId)
                .Select(x => (Guid?)x.ProjectId)
                .FirstOrDefaultAsync(ct),
            _ => null,
        };

    internal sealed class CommandHandler<TCommand>(
        ICommandHandler<TCommand> innerHandler, DocumentsDbContext db, IProjectDirectory projects,
        IProjectAccessGuard guard, IProjectAccessLevelReader levels, ICurrentUser currentUser)
        : ICommandHandler<TCommand>
        where TCommand : ICommand, IDocumentsScoped
    {
        public async Task<Result> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            Result check = await CheckAsync(command, isWrite: true, db, projects, guard, levels, currentUser, ct);
            return check.IsFailure ? check : await innerHandler.HandleAsync(command, ct);
        }
    }

    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> innerHandler, DocumentsDbContext db, IProjectDirectory projects,
        IProjectAccessGuard guard, IProjectAccessLevelReader levels, ICurrentUser currentUser)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>, IDocumentsScoped
    {
        public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            Result check = await CheckAsync(command, isWrite: true, db, projects, guard, levels, currentUser, ct);
            return check.IsFailure
                ? Result.Failure<TResponse>(check.Error)
                : await innerHandler.HandleAsync(command, ct);
        }
    }

    internal sealed class QueryHandler<TQuery, TResponse>(
        IQueryHandler<TQuery, TResponse> innerHandler, DocumentsDbContext db, IProjectDirectory projects,
        IProjectAccessGuard guard, IProjectAccessLevelReader levels, ICurrentUser currentUser)
        : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>, IDocumentsScoped
    {
        public async Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken ct = default)
        {
            Result check = await CheckAsync(query, isWrite: false, db, projects, guard, levels, currentUser, ct);
            return check.IsFailure
                ? Result.Failure<TResponse>(check.Error)
                : await innerHandler.HandleAsync(query, ct);
        }
    }
}
