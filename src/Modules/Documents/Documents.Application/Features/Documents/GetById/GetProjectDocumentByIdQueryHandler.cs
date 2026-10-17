using BuildingBlocks.Messaging;
using Documents.Contracts.Documents;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Documents.Application.Features.Documents.GetById;

internal sealed class GetProjectDocumentByIdQueryHandler(DocumentsDbContext db)
    : IQueryHandler<GetProjectDocumentByIdQuery, ProjectDocumentResponse>
{
    public async Task<Result<ProjectDocumentResponse>> HandleAsync(
        GetProjectDocumentByIdQuery query, CancellationToken ct = default)
    {
        ProjectDocumentResponse? document = await db.ProjectDocuments.AsNoTracking()
            .Where(d => d.Id == query.DocumentId && d.IsActive)
            .Select(ToResponse())
            .FirstOrDefaultAsync(ct);

        return document is null ? ProjectDocumentErrors.NotFound : document;
    }

    internal static System.Linq.Expressions.Expression<Func<ProjectDocument, ProjectDocumentResponse>> ToResponse() => d =>
        new ProjectDocumentResponse(
            d.Id, d.ProjectId, d.FolderId, d.ProjectStageId, d.ProjectTaskId, d.FileName, d.ContentType,
            d.FileSizeBytes, d.FileExtension, d.Description, d.UploadedById, d.UploadedOnUtc, d.Version,
            d.RootDocumentId, d.IsLocked);
}
