using BuildingBlocks.Messaging;
using Documents.Application.Features.Documents.GetById;
using Documents.Contracts.Documents;
using Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Documents.Application.Features.Documents.GetByFolder;

internal sealed class GetProjectDocumentsByFolderQueryHandler(DocumentsDbContext db)
    : IQueryHandler<GetProjectDocumentsByFolderQuery, IReadOnlyList<ProjectDocumentResponse>>
{
    public async Task<Result<IReadOnlyList<ProjectDocumentResponse>>> HandleAsync(
        GetProjectDocumentsByFolderQuery query, CancellationToken ct = default)
    {
        List<ProjectDocumentResponse> documents = await db.ProjectDocuments.AsNoTracking()
            .Where(d => d.FolderId == query.FolderId && d.IsActive)
            .OrderByDescending(d => d.UploadedOnUtc)
            .Select(GetProjectDocumentByIdQueryHandler.ToResponse())
            .ToListAsync(ct);

        return documents;
    }
}
