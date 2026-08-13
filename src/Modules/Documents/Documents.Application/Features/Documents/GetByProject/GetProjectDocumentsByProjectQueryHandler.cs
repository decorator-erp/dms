using BuildingBlocks.Messaging;
using Documents.Application.Features.Documents.GetById;
using Documents.Contracts.Documents;
using Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Documents.Application.Features.Documents.GetByProject;

internal sealed class GetProjectDocumentsByProjectQueryHandler(DocumentsDbContext db)
    : IQueryHandler<GetProjectDocumentsByProjectQuery, IReadOnlyList<ProjectDocumentResponse>>
{
    public async Task<Result<IReadOnlyList<ProjectDocumentResponse>>> HandleAsync(
        GetProjectDocumentsByProjectQuery query, CancellationToken ct = default)
    {
        List<ProjectDocumentResponse> documents = await db.ProjectDocuments.AsNoTracking()
            .Where(d => d.ProjectId == query.ProjectId && d.IsActive)
            .OrderByDescending(d => d.UploadedOnUtc)
            .Select(GetProjectDocumentByIdQueryHandler.ToResponse())
            .ToListAsync(ct);

        return documents;
    }
}
