using BuildingBlocks.Messaging;
using Documents.Application.Features.Folders.GetByProject;
using Documents.Contracts.Folders;
using Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Documents.Application.Features.Folders.GetByTask;

internal sealed class GetDocumentFoldersByTaskQueryHandler(DocumentsDbContext db)
    : IQueryHandler<GetDocumentFoldersByTaskQuery, IReadOnlyList<DocumentFolderResponse>>
{
    public async Task<Result<IReadOnlyList<DocumentFolderResponse>>> HandleAsync(
        GetDocumentFoldersByTaskQuery query, CancellationToken ct = default)
    {
        List<DocumentFolderResponse> folders = await db.DocumentFolders.AsNoTracking()
            .Where(f => f.ProjectTaskId == query.ProjectTaskId && f.IsActive)
            .OrderBy(f => f.Name)
            .Select(GetDocumentFoldersByProjectQueryHandler.ToResponse())
            .ToListAsync(ct);

        return folders;
    }
}
