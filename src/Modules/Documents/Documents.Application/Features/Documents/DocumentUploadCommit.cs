using BuildingBlocks.Auditing;
using BuildingBlocks.Outbox;
using Documents.Contracts;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using Notifications.Contracts;

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
///
/// <para>S11: with an <see cref="UploadActivity"/>, the same save also stages an
/// <see cref="ActivityRecordedIntegrationEvent"/> for the project, stage or task the file went onto, so the
/// project timeline shows the upload exactly when it committed.</para>
internal static class DocumentUploadCommit
{
    public static async Task CommitAsync(
        DocumentsDbContext db, IFileStorageService fileStorage, string storagePath,
        ProjectDocument document, CancellationToken ct, UploadActivity? activity = null)
    {
        db.ProjectDocuments.Add(document);
        db.Outbox().Enqueue(new DocumentUploadedIntegrationEvent(
            Guid.NewGuid(), document.Id, document.ProjectId, document.ProjectStageId, document.ProjectTaskId,
            document.FolderId, document.FileName, document.Version, document.UploadedById, DateTimeOffset.UtcNow));
        if (activity is not null)
        {
            string message = document.Version > 1
                ? $"Uploaded version {document.Version} of document '{document.FileName}'"
                : $"Uploaded document '{document.FileName}'";
            db.Outbox().Enqueue(new ActivityRecordedIntegrationEvent(
                Guid.NewGuid(), activity.ActorUserId, AuditAction.Update, activity.EntityName, activity.EntityId.ToString(),
                activity.EntityDisplayName, document.ProjectId, message, DateTimeOffset.UtcNow));
        }

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

/// <summary>S11: who uploaded, and onto which timeline entity (<see cref="ActivityEntityNames"/>).</summary>
internal sealed record UploadActivity(string ActorUserId, string EntityName, Guid EntityId, string? EntityDisplayName)
{
    /// <summary>The most specific entity a document sits on: its task, else its stage, else its project.</summary>
    public static UploadActivity For(string actorUserId, ProjectDocument document, string? displayName) =>
        document.ProjectTaskId is { } taskId ? new(actorUserId, ActivityEntityNames.ProjectTask, taskId, displayName)
        : document.ProjectStageId is { } stageId ? new(actorUserId, ActivityEntityNames.ProjectStage, stageId, displayName)
        : new(actorUserId, ActivityEntityNames.Project, document.ProjectId, displayName);
}
