using SharedKernel;

namespace Documents.Domain;

internal static class ProjectDocumentErrors
{
    public static readonly Error NotFound = Error.NotFound("project_documents.NotFound", "Document was not found.");

    /// <summary>BF-1: a Documents write needs project level Editor or higher. A Viewer can see the project, so this
    /// is a 403 about the caller, not the 404 a caller who cannot reach the project gets.</summary>
    public static readonly Error InsufficientLevel = Error.Forbidden(
        "project_documents.InsufficientLevel", "Your access level on this project does not allow this action.");

    public static readonly Error FileNameRequired = Error.BadRequest(
        "project_documents.FileNameRequired", "A file name is required.");

    public static readonly Error InvalidFileSize = Error.BadRequest(
        "project_documents.InvalidFileSize", "File size must be greater than zero.");

    /// <summary>Blocks move, delete, and a further review request. Does not block a new revision — see
    /// <see cref="ProjectDocument.IsLocked"/>'s own remarks.</summary>
    public static readonly Error Locked = Error.Conflict(
        "project_documents.Locked", "This document is locked pending an approved review and cannot be modified.");

    public static readonly Error AlreadyLocked = Error.Conflict(
        "project_documents.AlreadyLocked", "This document is already locked.");

    public static readonly Error FolderScopeMismatch = Error.BadRequest(
        "project_documents.FolderScopeMismatch", "The specified folder does not match this document's stage or task scope.");

    public static readonly Error ReviewInProgress = Error.Conflict(
        "project_documents.ReviewInProgress", "This document lineage already has an open review request.");

    public static readonly Error NotLatestVersion = Error.Conflict(
        "project_documents.NotLatestVersion", "Only the latest version in a document's lineage can be reviewed.");

    /// <summary>The acting user (resolved via <c>Hr.Contracts.IHrDirectory</c> from their Identity user
    /// id) has no Employee record — added at G7-07 alongside the upload commands' actor field.</summary>
    public static readonly Error UploaderNotFound = Error.BadRequest(
        "project_documents.UploaderNotFound", "The uploading user has no employee record.");
}

internal static class DocumentFolderErrors
{
    public static readonly Error NotFound = Error.NotFound("document_folders.NotFound", "Folder was not found.");

    public static readonly Error NameRequired = Error.BadRequest(
        "document_folders.NameRequired", "A folder name is required.");

    /// <summary>At most one of stage/task may be set — neither is valid (project-wide); both is not.
    /// Not a strict XOR, unlike <c>GateConditionErrors.InvalidScope</c>.</summary>
    public static readonly Error InvalidScope = Error.BadRequest(
        "document_folders.InvalidScope", "A folder may be scoped to a stage or a task, but not both.");

    public static readonly Error DefaultAlreadyExists = Error.Conflict(
        "document_folders.DefaultAlreadyExists", "A default folder already exists for this stage or task.");

    /// <summary>New work — the backend has no cycle guard at all (G7-02's correction 5). Covers both the
    /// direct self-move and a move under one of this folder's own descendants.</summary>
    public static readonly Error CyclicMove = Error.BadRequest(
        "document_folders.CyclicMove", "A folder cannot be moved under itself or one of its own subfolders.");

    public static readonly Error NameCollision = Error.Conflict(
        "document_folders.NameCollision", "A folder with this name already exists at this location.");
}

/// <summary>Errors for <c>DocumentApprovedHandler</c>'s create-time gate validation (G7-07) — kept
/// separate from <see cref="ProjectDocumentErrors"/> since these describe a GATE reference problem, not
/// a document operation. The backend's equivalent handler returns Projects' own domain errors, which
/// this module cannot reference (Documents.Application has no visibility into Projects.Domain,
/// internal and cross-module) — these are Documents' own.</summary>
internal static class GateReferenceErrors
{
    public static readonly Error InvalidReference = Error.BadRequest(
        "gate_reference.InvalidReference", "A referenced document is required for this condition type.");

    public static readonly Error DocumentNotFound = Error.NotFound(
        "gate_reference.DocumentNotFound", "The referenced document lineage was not found.");

    public static readonly Error CrossProjectReference = Error.BadRequest(
        "gate_reference.CrossProjectReference", "A gate condition cannot reference a document in another project.");
}

internal static class FileTemplateErrors
{
    public static readonly Error NotFound = Error.NotFound("file_templates.NotFound", "File template was not found.");

    public static readonly Error NameRequired = Error.BadRequest(
        "file_templates.NameRequired", "A template name is required.");

    public static readonly Error NameAlreadyExists = Error.Conflict(
        "file_templates.NameAlreadyExists", "A file template with this name already exists.");

    public static readonly Error InvalidFileSize = Error.BadRequest(
        "file_templates.InvalidFileSize", "File size must be greater than zero.");
}
