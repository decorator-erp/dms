using BuildingBlocks.Events;

namespace Documents.Contracts;

/// <summary>
/// Published by every upload command (<c>UploadProjectDocumentCommand</c>,
/// <c>UploadStageDocumentCommand</c>, <c>UploadTaskDocumentCommand</c>,
/// <c>UploadDocumentRevisionCommand</c>, <c>ApplyFileTemplateToTaskCommand</c> — G7-07), alongside the
/// row's own insert, in the same <c>SaveChangesAsync</c>. IDs and primitives only, per rule 5.
///
/// This is structural item 2's direction: <c>Projects.Infrastructure</c> subscribes
/// (<see cref="IIntegrationEventHandler{TEvent}"/>, G7-07) to satisfy open <c>Attachment</c> gates for
/// the uploaded document's stage/task — Documents publishes with zero knowledge of who reacts, per
/// <c>docs/adding-a-module.md</c>'s cross-module-write pattern. <c>Version</c> distinguishes an initial
/// upload (1) from a revision (&gt;1) for a subscriber that only cares about the first.
/// </summary>
public sealed record DocumentUploadedIntegrationEvent(
    Guid EventId,
    Guid DocumentId,
    Guid ProjectId,
    Guid? ProjectStageId,
    Guid? ProjectTaskId,
    Guid? FolderId,
    string FileName,
    int Version,
    Guid UploadedById,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
