using BuildingBlocks.Outbox;
using Documents.Contracts;
using Documents.Domain;
using Documents.Infrastructure.Persistence;

namespace Documents.Application.Features.Documents;

/// <summary>
/// The upload transaction shape every Files command shares (G7-07): the blob is already written to disk
/// (by the time this runs) before the row exists — this only adds the row, enqueues
/// <see cref="DocumentUploadedIntegrationEvent"/> in the SAME <c>SaveChangesAsync</c>, and deletes the
/// just-written blob if that commit fails for any reason. Extracted once here rather than copied across
/// <c>UploadProjectDocumentCommand</c>/<c>UploadStageDocumentCommand</c>/<c>UploadTaskDocumentCommand</c>/
/// <c>UploadDocumentRevisionCommand</c>/<c>ApplyFileTemplateToTaskCommand</c> — the backend repeats this
/// shape four separate times.
/// </summary>
internal static class DocumentUploadCommit
{
    public static async Task CommitAsync(
        DocumentsDbContext db, IFileStorageService fileStorage, string storagePath,
        ProjectDocument document, CancellationToken ct)
    {
        db.ProjectDocuments.Add(document);
        db.Outbox().Enqueue(new DocumentUploadedIntegrationEvent(
            Guid.NewGuid(), document.Id, document.ProjectId, document.ProjectStageId, document.ProjectTaskId,
            document.FolderId, document.FileName, document.Version, document.UploadedById, DateTimeOffset.UtcNow));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await fileStorage.DeleteAsync(storagePath, ct);
            throw;
        }
    }
}
