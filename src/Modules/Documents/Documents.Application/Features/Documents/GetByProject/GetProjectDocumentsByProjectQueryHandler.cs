using BuildingBlocks;
using BuildingBlocks.Messaging;
using Documents.Application.Features.Documents.GetById;
using Documents.Contracts.Documents;
using Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Documents.Application.Features.Documents.GetByProject;

/// <summary>P1-A: rows outside a stage-limited grant's stages are dropped (<see cref="DocumentStageFilter"/>).</summary>
internal sealed class GetProjectDocumentsByProjectQueryHandler(
    DocumentsDbContext db, IProjectAccessGuard guard, IProjectDirectory projects, ICurrentUser currentUser)
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

        return await DocumentStageFilter.ApplyAsync(
                guard, projects, currentUser, query.ProjectId, documents, d => d.ProjectStageId, d => d.ProjectTaskId, ct);
    }
}
