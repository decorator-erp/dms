using BuildingBlocks.Messaging;
using Documents.Contracts;
using Documents.Contracts.Documents;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Documents.Application.Features.Documents.Download;

/// <summary>Streams straight from <see cref="IFileStorageService.OpenReadAsync"/> — never buffers the
/// whole file, per the plan's own hosting-target hardening note (G7-08's endpoint is what actually
/// avoids buffering; this handler just doesn't introduce any).</summary>
internal sealed class GetProjectDocumentDownloadQueryHandler(DocumentsDbContext db, IFileStorageService fileStorage)
    : IQueryHandler<GetProjectDocumentDownloadQuery, ProjectDocumentDownloadResponse>
{
    public async Task<Result<ProjectDocumentDownloadResponse>> HandleAsync(
        GetProjectDocumentDownloadQuery query, CancellationToken ct = default)
    {
        var document = await db.ProjectDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == query.DocumentId && d.IsActive, ct);
        if (document is null)
            return ProjectDocumentErrors.NotFound;

        var content = await fileStorage.OpenReadAsync(document.StoragePath, ct);
        if (content is null)
            return ProjectDocumentErrors.NotFound;

        return new ProjectDocumentDownloadResponse(document.FileName, document.ContentType, content);
    }
}
