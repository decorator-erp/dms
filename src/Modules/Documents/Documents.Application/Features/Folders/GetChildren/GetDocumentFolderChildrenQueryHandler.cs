using BuildingBlocks;
using BuildingBlocks.Messaging;
using Documents.Application.Features.Folders.GetByProject;
using Documents.Contracts.Folders;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Documents.Application.Features.Folders.GetChildren;

/// <summary>P1-A: folders outside a stage-limited grant's stages are dropped (<see cref="DocumentStageFilter"/>).</summary>
internal sealed class GetDocumentFolderChildrenQueryHandler(
    DocumentsDbContext db, IProjectAccessGuard guard, IProjectDirectory projects, ICurrentUser currentUser)
    : IQueryHandler<GetDocumentFolderChildrenQuery, IReadOnlyList<DocumentFolderResponse>>
{
    public async Task<Result<IReadOnlyList<DocumentFolderResponse>>> HandleAsync(
        GetDocumentFolderChildrenQuery query, CancellationToken ct = default)
    {
        bool parentExists = await db.DocumentFolders.AsNoTracking().AnyAsync(f => f.Id == query.FolderId, ct);
        if (!parentExists)
        {
            return DocumentFolderErrors.NotFound;
        }

        List<DocumentFolderResponse> children = await db.DocumentFolders.AsNoTracking()
            .Where(f => f.ParentFolderId == query.FolderId && f.IsActive)
            .OrderBy(f => f.Name)
            .Select(GetDocumentFoldersByProjectQueryHandler.ToResponse())
            .ToListAsync(ct);

        return children.Count == 0
            ? children
            : await DocumentStageFilter.ApplyAsync(
                guard, projects, currentUser, children[0].ProjectId, children, f => f.ProjectStageId, f => f.ProjectTaskId, ct);
    }
}
