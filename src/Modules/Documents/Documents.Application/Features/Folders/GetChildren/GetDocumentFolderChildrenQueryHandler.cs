using BuildingBlocks.Messaging;
using Documents.Application.Features.Folders.GetByProject;
using Documents.Contracts.Folders;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Documents.Application.Features.Folders.GetChildren;

internal sealed class GetDocumentFolderChildrenQueryHandler(DocumentsDbContext db)
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

        return children;
    }
}
