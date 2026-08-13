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

/// <summary>
/// Published by Documents' own <c>ApprovalDecidedIntegrationEventHandler</c> (G7-07) when a
/// <c>ProjectDocument</c>-subject approval decides Approved — the SAME bidirectional shape
/// <see cref="DocumentUploadedIntegrationEvent"/> already establishes for <c>Attachment</c> gates, one
/// condition type over: Documents cannot satisfy a <c>GateCondition</c> directly (it lives in
/// <c>projects.</c>, never touched from here), so it publishes this instead and
/// <c>Projects.Infrastructure</c> subscribes to satisfy matching <c>DocumentApproved</c> gates
/// (<c>ConditionType.DocumentApproved &amp;&amp; ReferencedDocumentId == RootDocumentId</c>).
/// <see cref="RootDocumentId"/>, not the specific decided version's id — a gate waits on the LINEAGE,
/// and <c>GateCondition.ReferencedDocumentId</c> (G7-02's A1) stores the root for exactly this reason.
/// </summary>
public sealed record DocumentApprovedIntegrationEvent(
    Guid EventId,
    Guid RootDocumentId,
    Guid ProjectId,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
