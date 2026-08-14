using BuildingBlocks.Messaging;
using Documents.Application.Features.Documents.GetById;
using Documents.Contracts.Documents;
using Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Documents.Application.Features.Documents.GetByStage;

/// <summary>Returns documents scoped to the stage directly AND those uploaded to a task under it — the
/// one predicate covers both because a task upload denormalizes its owning stage onto the row (E1-01,
/// closing G7-02's correction 8). Must stay in lockstep with <c>DocumentQueries.ForStageAsync</c>: the
/// two are the same question asked through different surfaces, and they disagreeing about what "stage
/// documents" means is the bug E1-01 fixed.</summary>
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
