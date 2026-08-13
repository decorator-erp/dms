using BuildingBlocks.Messaging;
using Documents.Application.Features.Documents.GetById;
using Documents.Contracts.Documents;
using Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Documents.Application.Features.Documents.GetByTask;

internal sealed class GetProjectDocumentsByTaskQueryHandler(DocumentsDbContext db)
    : IQueryHandler<GetProjectDocumentsByTaskQuery, IReadOnlyList<ProjectDocumentResponse>>
{
    public async Task<Result<IReadOnlyList<ProjectDocumentResponse>>> HandleAsync(
        GetProjectDocumentsByTaskQuery query, CancellationToken ct = default)
    {
        List<ProjectDocumentResponse> documents = await db.ProjectDocuments.AsNoTracking()
            .Where(d => d.ProjectTaskId == query.ProjectTaskId && d.IsActive)
            .OrderByDescending(d => d.UploadedOnUtc)
            .Select(GetProjectDocumentByIdQueryHandler.ToResponse())
            .ToListAsync(ct);

        return documents;
    }
}
