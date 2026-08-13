using BuildingBlocks.Messaging;
using Documents.Application.Features.Documents.GetById;
using Documents.Contracts.Documents;
using Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Documents.Application.Features.Documents.GetByStage;

/// <summary>Directly stage-scoped only — see <c>GetProjectDocumentsByStageQuery</c>'s own remarks for
/// why the task-under-this-stage reach-through lives on the gate-satisfaction side (G7-07) instead.</summary>
internal sealed class GetProjectDocumentsByStageQueryHandler(DocumentsDbContext db)
    : IQueryHandler<GetProjectDocumentsByStageQuery, IReadOnlyList<ProjectDocumentResponse>>
{
    public async Task<Result<IReadOnlyList<ProjectDocumentResponse>>> HandleAsync(
        GetProjectDocumentsByStageQuery query, CancellationToken ct = default)
    {
        List<ProjectDocumentResponse> documents = await db.ProjectDocuments.AsNoTracking()
            .Where(d => d.ProjectStageId == query.ProjectStageId && d.IsActive)
            .OrderByDescending(d => d.UploadedOnUtc)
            .Select(GetProjectDocumentByIdQueryHandler.ToResponse())
            .ToListAsync(ct);

        return documents;
    }
}
