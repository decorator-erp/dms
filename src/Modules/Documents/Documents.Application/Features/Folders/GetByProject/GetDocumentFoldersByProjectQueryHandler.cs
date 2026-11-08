using BuildingBlocks;
using BuildingBlocks.Messaging;
using Documents.Contracts.Folders;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Documents.Application.Features.Folders.GetByProject;

/// <summary>P1-A: folders outside a stage-limited grant's stages are dropped (<see cref="DocumentStageFilter"/>).</summary>
internal sealed class GetDocumentFoldersByProjectQueryHandler(
    DocumentsDbContext db, IProjectAccessGuard guard, IProjectDirectory projects, ICurrentUser currentUser)
    : IQueryHandler<GetDocumentFoldersByProjectQuery, IReadOnlyList<DocumentFolderResponse>>
{
    public async Task<Result<IReadOnlyList<DocumentFolderResponse>>> HandleAsync(
        GetDocumentFoldersByProjectQuery query, CancellationToken ct = default)
    {
        List<DocumentFolderResponse> folders = await db.DocumentFolders.AsNoTracking()
            .Where(f => f.ProjectId == query.ProjectId && f.IsActive)
            .OrderBy(f => f.Name)
            .Select(ToResponse())
            .ToListAsync(ct);

        return await DocumentStageFilter.ApplyAsync(
                guard, projects, currentUser, query.ProjectId, folders, f => f.ProjectStageId, f => f.ProjectTaskId, ct);
    }

    internal static System.Linq.Expressions.Expression<Func<DocumentFolder, DocumentFolderResponse>> ToResponse() => f =>
        new DocumentFolderResponse(f.Id, f.Name, f.ProjectId, f.ParentFolderId, f.ProjectStageId, f.ProjectTaskId, f.IsDefault);
}
