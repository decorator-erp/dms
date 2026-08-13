using BuildingBlocks.Messaging;
using Documents.Application.Features.Folders.GetByProject;
using Documents.Contracts.Folders;
using Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Documents.Application.Features.Folders.GetByStage;

internal sealed class GetDocumentFoldersByStageQueryHandler(DocumentsDbContext db)
    : IQueryHandler<GetDocumentFoldersByStageQuery, IReadOnlyList<DocumentFolderResponse>>
{
    public async Task<Result<IReadOnlyList<DocumentFolderResponse>>> HandleAsync(
        GetDocumentFoldersByStageQuery query, CancellationToken ct = default)
    {
        List<DocumentFolderResponse> folders = await db.DocumentFolders.AsNoTracking()
            .Where(f => f.ProjectStageId == query.ProjectStageId && f.IsActive)
            .OrderBy(f => f.Name)
            .Select(GetDocumentFoldersByProjectQueryHandler.ToResponse())
            .ToListAsync(ct);

        return folders;
    }
}
