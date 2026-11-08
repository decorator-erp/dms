using BuildingBlocks;
using BuildingBlocks.Messaging;
using Documents.Application.Features.Documents.GetById;
using Documents.Contracts.Documents;
using Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Documents.Application.Features.Documents.GetByFolder;

/// <summary>P1-A: rows outside a stage-limited grant's stages are dropped (<see cref="DocumentStageFilter"/>).</summary>
internal sealed class GetProjectDocumentsByFolderQueryHandler(
    DocumentsDbContext db, IProjectAccessGuard guard, IProjectDirectory projects, ICurrentUser currentUser)
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

        return documents.Count == 0
            ? documents
            : await DocumentStageFilter.ApplyAsync(
                guard, projects, currentUser, documents[0].ProjectId, documents, d => d.ProjectStageId, d => d.ProjectTaskId, ct);
    }
}
