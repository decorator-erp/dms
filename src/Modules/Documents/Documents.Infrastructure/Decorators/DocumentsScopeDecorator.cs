using BuildingBlocks;
using BuildingBlocks.Messaging;
using Documents.Contracts;
using Documents.Contracts.Folders;
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
///
/// <para><b>P1-A: project access grants.</b> Every check names the <see cref="ProjectAccessArea.Documents"/> area and
/// the stage the thing belongs to (its own stage, or its task's). A grant limited to some stages reaches only those
/// stages' documents and folders; a stage-less document or folder needs a whole-project grant, except that a
/// project-level folder can be opened as a list, which the handler filters. Writes need a grant of Editor or more
/// on that stage. The list handlers keep only the rows <see cref="IProjectAccessGuard.GetStageFilterAsync"/>
/// allows (<c>DocumentStageFilter</c>).</para>
/// </summary>
internal static class DocumentsScopeDecorator
{
    /// <summary>P1-A: the project a message resolves to, the stage it is about (its own, or its task's), and whether
    /// it reads a list the handler filters by stage (the project's documents and folders, and a folder that belongs to
    /// no stage).</summary>
    private readonly record struct Target(Guid ProjectId, Guid? StageId, bool IsList);

    private static async Task<Result> CheckAsync(
        IDocumentsScoped scoped, bool isWrite, DocumentsDbContext db, IProjectDirectory projects,
        IProjectAccessGuard guard, IProjectAccessLevelReader levels, ICurrentUser currentUser, CancellationToken ct)
    {
        Error notFound = scoped is IDocumentsFolderScoped ? DocumentFolderErrors.NotFound : ProjectDocumentErrors.NotFound;

        Target? target = await ResolveAsync(scoped, isWrite, db, projects, ct);
        if (target is not { } t || string.IsNullOrEmpty(currentUser.UserId))
        {
            return notFound;
        }

        if (!isWrite)
        {
            bool canRead = t.IsList
                ? await guard.GetStageFilterAsync(t.ProjectId, currentUser.UserId, ProjectAccessArea.Documents, ct) is not { Count: 0 }
                : (await guard.EnsureProjectAccessAsync(t.ProjectId, currentUser.UserId, ProjectAccessArea.Documents, t.StageId, ct)).IsSuccess;
            return canRead ? Result.Success() : notFound;
        }

        ProjectMemberAccess? level = await levels.GetLevelAsync(
            t.ProjectId, currentUser.UserId, ProjectAccessArea.Documents, t.StageId, ct);
        if (level is null)
        {
            return notFound;
        }

        return level.Value < ProjectMemberAccess.Editor ? ProjectDocumentErrors.InsufficientLevel : Result.Success();
    }

    private static async Task<Target?> ResolveAsync(
        IDocumentsScoped scoped, bool isWrite, DocumentsDbContext db, IProjectDirectory projects, CancellationToken ct)
    {
        switch (scoped)
        {
            case CreateDocumentFolderCommand folder:
                return new Target(folder.ProjectId, folder.ProjectStageId ?? await StageOfTaskAsync(folder.ProjectTaskId, projects, ct), false);
            case IDocumentsProjectScoped p:
                return new Target(p.ProjectId, null, !isWrite);
            case IDocumentsStageScoped s:
                return await projects.GetStageAsync(s.ProjectStageId, ct) is { } stage
                    ? new Target(stage.ProjectId, stage.Id, false)
                    : null;
            case IDocumentsTaskScoped tk:
                return await projects.GetTaskAsync(tk.ProjectTaskId, ct) is { } task
                    ? new Target(task.ProjectId, task.ProjectStageId, false)
                    : null;
            case IDocumentsFolderScoped f:
                {
                    var folder = await db.DocumentFolders.AsNoTracking()
                        .Where(x => x.Id == f.FolderId)
                        .Select(x => new { x.ProjectId, x.ProjectStageId, x.ProjectTaskId })
                        .FirstOrDefaultAsync(ct);
                    if (folder is null)
                    {
                        return null;
                    }

                    Guid? folderStage = folder.ProjectStageId ?? await StageOfTaskAsync(folder.ProjectTaskId, projects, ct);
                    return new Target(folder.ProjectId, folderStage, !isWrite && folderStage is null);
                }

            case IDocumentsDocumentScoped d:
                {
                    var document = await db.ProjectDocuments.AsNoTracking()
                        .Where(x => x.Id == d.DocumentId)
                        .Select(x => new { x.ProjectId, x.ProjectStageId, x.ProjectTaskId })
                        .FirstOrDefaultAsync(ct);
                    return document is null
                        ? null
                        : new Target(
                            document.ProjectId, document.ProjectStageId ?? await StageOfTaskAsync(document.ProjectTaskId, projects, ct), false);
                }

            default:
                return null;
        }
    }

    private static async Task<Guid?> StageOfTaskAsync(Guid? taskId, IProjectDirectory projects, CancellationToken ct) =>
        taskId is { } id ? (await projects.GetTaskAsync(id, ct))?.ProjectStageId : null;

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
