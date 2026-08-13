using SharedKernel;

namespace Documents.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/ProjectDocument.cs</c>, with two deliberate corrections
/// recorded at G7-02:
/// <list type="bullet">
/// <item><see cref="RootDocumentId"/>/<see cref="Version"/> is the lineage pair, not <see cref="Version"/>
/// alone — a v1 document's <see cref="RootDocumentId"/> equals its own <see cref="SharedKernel.Entity.Id"/>
/// (self-referencing), and every later revision (<see cref="CreateRevision"/>) carries the SAME root with
/// <see cref="Version"/> incremented. There is no parent-document id, only root + version.</item>
/// <item><see cref="IsLocked"/> blocks <see cref="Lock"/> being called twice, move, and delete — set by
/// the approval flow when a <c>ProjectDocument</c>-subject review is approved (G7-07). Does NOT block a
/// further revision upload.</item>
/// </list>
/// Every <c>Project</c>/<c>ProjectStage</c>/<c>ProjectTask</c>/<c>Employee</c> reference is a bare
/// <see cref="Guid"/>, never a navigation — cross-module, per the reference matrix.
/// </summary>
internal sealed class ProjectDocument : Entity, IAuditable
{
    public Guid ProjectId { get; private set; }
    public Guid? FolderId { get; private set; }
    public Guid? ProjectStageId { get; private set; }
    public Guid? ProjectTaskId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string StoragePath { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long FileSizeBytes { get; private set; }
    public string FileExtension { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid UploadedById { get; private set; }
    public DateTime UploadedOnUtc { get; private set; }
    public int Version { get; private set; }
    public Guid RootDocumentId { get; private set; }
    public bool IsLocked { get; private set; }

    private ProjectDocument()
    {
        // EF materialization.
    }

    public static Result<ProjectDocument> Create(
        Guid projectId, string fileName, string storagePath, string contentType, long fileSizeBytes,
        Guid uploadedById, Guid? folderId = null, Guid? projectStageId = null, Guid? projectTaskId = null,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return ProjectDocumentErrors.FileNameRequired;

        if (fileSizeBytes <= 0)
            return ProjectDocumentErrors.InvalidFileSize;

        var document = new ProjectDocument
        {
            ProjectId = projectId,
            FolderId = folderId,
            ProjectStageId = projectStageId,
            ProjectTaskId = projectTaskId,
            FileName = fileName,
            StoragePath = storagePath,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            FileSizeBytes = fileSizeBytes,
            FileExtension = Path.GetExtension(fileName).ToLowerInvariant(),
            Description = description,
            UploadedById = uploadedById,
            UploadedOnUtc = DateTime.UtcNow,
            Version = 1,
            IsLocked = false,
        };
        // v1 self-references as its own lineage root - see the type-level remarks.
        document.RootDocumentId = document.Id;
        document.AddDomainEvent(new DocumentUploadedDomainEvent(document.Id));

        return document;
    }

    /// <summary>New row, same lineage. <paramref name="previous"/> must be the CURRENT latest revision
    /// in its lineage — the caller (Application, which has DbContext access this entity doesn't) is
    /// responsible for that lookup; this factory only enforces the lock.</summary>
    public static Result<ProjectDocument> CreateRevision(
        ProjectDocument previous, string fileName, string storagePath, string contentType, long fileSizeBytes,
        Guid uploadedById, string? description = null)
    {
        if (previous.IsLocked)
            return ProjectDocumentErrors.Locked;

        if (string.IsNullOrWhiteSpace(fileName))
            return ProjectDocumentErrors.FileNameRequired;

        if (fileSizeBytes <= 0)
            return ProjectDocumentErrors.InvalidFileSize;

        var revision = new ProjectDocument
        {
            ProjectId = previous.ProjectId,
            FolderId = previous.FolderId,
            ProjectStageId = previous.ProjectStageId,
            ProjectTaskId = previous.ProjectTaskId,
            FileName = fileName,
            StoragePath = storagePath,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            FileSizeBytes = fileSizeBytes,
            FileExtension = Path.GetExtension(fileName).ToLowerInvariant(),
            Description = description ?? previous.Description,
            UploadedById = uploadedById,
            UploadedOnUtc = DateTime.UtcNow,
            Version = previous.Version + 1,
            RootDocumentId = previous.RootDocumentId,
            IsLocked = false,
        };
        revision.AddDomainEvent(new DocumentUploadedDomainEvent(revision.Id));

        return revision;
    }

    public Result MoveToFolder(Guid? folderId)
    {
        if (IsLocked)
            return ProjectDocumentErrors.Locked;

        FolderId = folderId;
        return Result.Success();
    }

    public Result UpdateDescription(string? description)
    {
        if (IsLocked)
            return ProjectDocumentErrors.Locked;

        Description = description;
        return Result.Success();
    }

    /// <summary>Set when a <c>ProjectDocument</c>-subject approval decides Approved (G7-07). Blocks
    /// delete, move, and a further review request; does not block a new revision.</summary>
    public Result Lock()
    {
        if (IsLocked)
            return ProjectDocumentErrors.AlreadyLocked;

        IsLocked = true;
        return Result.Success();
    }

    /// <summary>Soft delete only — <see cref="SharedKernel.Entity.IsActive"/> flips to false, but the
    /// blob is deliberately KEPT (G7-02's correction 4): a soft-deleted row whose bytes are destroyed is
    /// not soft-deleted, it is a tombstone that lies, and undelete is the only reason <c>IsActive</c>
    /// exists here. Rejected while locked, same as move.</summary>
    public Result Deactivate()
    {
        if (IsLocked)
            return ProjectDocumentErrors.Locked;

        SetActive(false);
        return Result.Success();
    }

    public void Activate() => SetActive(true);
}
