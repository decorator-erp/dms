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
    string? Description = null) : ICommand<Guid>;

/// <summary>Copies a <c>FileTemplate</c>'s stored bytes into a new document attached to
/// <paramref name="ProjectTaskId"/>, always resolving/creating the task's default folder (unlike the
/// plain task upload, which only does so when no explicit folder is given).</summary>
public sealed record ApplyFileTemplateToTaskCommand(Guid ProjectTaskId, Guid FileTemplateId) : ICommand<Guid>;

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

/// <summary>Reach-through to task-scoped documents under this stage — same rule as
/// <see cref="IDocumentQueries.ForStageAsync"/>, and for the identical reason (an <c>Attachment</c> gate
/// on a stage must see documents attached at the task level too).</summary>
public sealed record GetProjectDocumentsByStageQuery(Guid ProjectStageId)
    : IQuery<IReadOnlyList<ProjectDocumentResponse>>;

public sealed record GetProjectDocumentsByTaskQuery(Guid ProjectTaskId)
    : IQuery<IReadOnlyList<ProjectDocumentResponse>>;

public sealed record GetProjectDocumentDownloadQuery(Guid DocumentId) : IQuery<ProjectDocumentDownloadResponse>;
