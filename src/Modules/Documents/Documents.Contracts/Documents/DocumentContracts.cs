using BuildingBlocks.Messaging;

namespace Documents.Contracts.Documents;

/// <summary>
/// Command/query wrapper types and DTOs for Documents' file features, living here rather than in
/// <c>Documents.Application</c> for the same reason <c>Hr.Contracts.Employees</c>'s own remarks explain
/// — <c>Documents.Infrastructure</c>'s endpoints need to name these exact types, and
/// <c>Documents.Application</c> already has a real <c>ProjectReference</c> to
/// <c>Documents.Infrastructure</c>, so the reverse reference would be circular.
///
/// <see cref="RootDocumentId"/>/<see cref="Version"/> are the lineage pair (G7-02's correction 1) —
/// <see cref="RootDocumentId"/> equals a v1 document's own id, and every later revision carries the
/// same root with an incremented <see cref="Version"/>. There is no parent-document id, only root +
/// version.
///
/// <c>UploadedByUserId</c> on every upload command is the Identity <c>ApplicationUser</c> id (a
/// string), never an Employee <c>Guid</c> directly — the same "carry the acting user's Identity id, let
/// the handler resolve it to an Employee via <c>Hr.Contracts.IHrDirectory</c>" pattern
/// <c>SatisfyGateConditionCommand.UserId</c> already establishes in Projects. An earlier draft of these
/// commands had no actor field at all, discovered missing while wiring the first upload handler at
/// G7-07 — fixed here before any handler shipped against the gap.
/// </summary>
public sealed record ProjectDocumentResponse(
    Guid Id,
    Guid ProjectId,
    Guid? FolderId,
    Guid? ProjectStageId,
    Guid? ProjectTaskId,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string FileExtension,
    string? Description,
    Guid UploadedById,
    DateTime UploadedOnUtc,
    int Version,
    Guid RootDocumentId,
    bool IsLocked);

/// <summary>Streamed straight to the response, never buffered — G7-08's upload/download endpoints must
/// stream rather than read a whole file into memory (the plan's own hosting-target hardening note).
/// The caller owns disposing <see cref="Content"/>.</summary>
public sealed record ProjectDocumentDownloadResponse(string FileName, string ContentType, Stream Content);

/// <summary><paramref name="Content"/>/<paramref name="FileName"/>/<paramref name="ContentType"/>/
/// <paramref name="FileSizeBytes"/> come from the multipart form, extracted by the endpoint before this
/// command is constructed — never bound automatically the way a JSON command is. The caller owns
/// disposing <paramref name="Content"/> once the handler returns.</summary>
public sealed record UploadProjectDocumentCommand(
    Guid ProjectId,
    Stream Content,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string UploadedByUserId,
    Guid? FolderId = null,
    string? Description = null) : ICommand<Guid>;

/// <summary>No explicit <c>FolderId</c> resolves via <c>DefaultFolderResolver.EnsureForStageAsync</c>
/// (G7-06) — an explicit one must match the stage's own scope or the handler rejects it
/// (<c>FolderScopeMismatch</c>, ported from the backend).</summary>
public sealed record UploadStageDocumentCommand(
    Guid ProjectStageId,
    Stream Content,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string UploadedByUserId,
    Guid? FolderId = null,
    string? Description = null) : ICommand<Guid>;

/// <summary>Same default-folder/scope-match rule as <see cref="UploadStageDocumentCommand"/>, resolved
/// via <c>DefaultFolderResolver.EnsureForTaskAsync</c>.</summary>
public sealed record UploadTaskDocumentCommand(
    Guid ProjectTaskId,
    Stream Content,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string UploadedByUserId,
    Guid? FolderId = null,
    string? Description = null) : ICommand<Guid>;

/// <summary>Creates a new lineage row via <c>ProjectDocument.CreateRevision</c> — same
/// <c>RootDocumentId</c>, <c>Version + 1</c>, same folder/stage/task scope as
/// <paramref name="DocumentId"/>'s current latest revision. Rejected if the lineage is locked (an
/// approved review in progress, G7-07) or already has an open review request.</summary>
public sealed record UploadDocumentRevisionCommand(
    Guid DocumentId,
    Stream Content,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string UploadedByUserId,
    string? Description = null) : ICommand<Guid>;

/// <summary>Copies a <c>FileTemplate</c>'s stored bytes into a new document attached to
/// <paramref name="ProjectTaskId"/>, always resolving/creating the task's default folder (unlike the
/// plain task upload, which only does so when no explicit folder is given).</summary>
public sealed record ApplyFileTemplateToTaskCommand(
    Guid ProjectTaskId, Guid FileTemplateId, string UploadedByUserId) : ICommand<Guid>;

public sealed record MoveProjectDocumentCommand(Guid DocumentId, Guid? FolderId) : ICommand;

/// <summary>Soft delete only (<c>Entity.IsActive = false</c>) — the row is recoverable, but per G7-02's
/// correction 4 the underlying blob is deliberately KEPT (not passed to
/// <see cref="IFileStorageService.DeleteAsync"/>), unlike the backend's own hard-delete-the-blob
/// behavior: a soft-deleted row whose bytes are destroyed is not soft-deleted, it is a tombstone that
/// lies.</summary>
public sealed record DeleteProjectDocumentCommand(Guid DocumentId) : ICommand;

public sealed record GetProjectDocumentByIdQuery(Guid DocumentId) : IQuery<ProjectDocumentResponse>;

public sealed record GetProjectDocumentsByProjectQuery(Guid ProjectId)
    : IQuery<IReadOnlyList<ProjectDocumentResponse>>;

public sealed record GetProjectDocumentsByFolderQuery(Guid FolderId)
    : IQuery<IReadOnlyList<ProjectDocumentResponse>>;

/// <summary>Directly stage-scoped documents only — see <c>IDocumentQueries.ForStageAsync</c>'s own
/// remarks for why the task-under-this-stage reach-through lives on the gate-satisfaction side (G7-07)
/// instead of here.</summary>
public sealed record GetProjectDocumentsByStageQuery(Guid ProjectStageId)
    : IQuery<IReadOnlyList<ProjectDocumentResponse>>;

public sealed record GetProjectDocumentsByTaskQuery(Guid ProjectTaskId)
    : IQuery<IReadOnlyList<ProjectDocumentResponse>>;

public sealed record GetProjectDocumentDownloadQuery(Guid DocumentId) : IQuery<ProjectDocumentDownloadResponse>;

/// <summary>
/// Not one of G7-01's original 31 — added at G7-07 because making
/// <c>ApprovalSubjectType.ProjectDocument</c> real (the plan's own G7-07 text) requires an actual way to
/// REQUEST a review, and none of the 31 counted features covered it. The backend resolves the reviewer
/// as "the project's client" via a CRM lookup this build doesn't have (CRM is Phase 4) — this port names
/// the approver explicitly instead (<paramref name="ApproverEmployeeId"/>), resolved to an
/// <c>Approvals.Contracts.ApproverSpec</c> via <c>Hr.Contracts.IHrDirectory</c>. Rejected if the
/// document is locked, isn't the latest version in its lineage, or already has an open review —
/// <c>ProjectDocumentErrors.Locked</c>/<c>NotLatestVersion</c>/<c>ReviewInProgress</c>, all already
/// defined at G7-02 for exactly this.
/// </summary>
public sealed record RequestDocumentReviewCommand(
    Guid DocumentId, Guid ApproverEmployeeId, string RequestedByUserId) : ICommand<Guid>;
