using BuildingBlocks.Messaging;
using Documents.Contracts.Folders;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Documents.Application.Features.Folders.GetByProject;

internal sealed class GetDocumentFoldersByProjectQueryHandler(DocumentsDbContext db)
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

        return folders;
    }

    internal static System.Linq.Expressions.Expression<Func<DocumentFolder, DocumentFolderResponse>> ToResponse() => f =>
        new DocumentFolderResponse(f.Id, f.Name, f.ProjectId, f.ParentFolderId, f.ProjectStageId, f.ProjectTaskId, f.IsDefault);
}
