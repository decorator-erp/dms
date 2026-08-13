using SharedKernel;

namespace Documents.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/DocumentFolder.cs</c>. <see cref="ProjectStageId"/>/
/// <see cref="ProjectTaskId"/> follow "at most one, or neither" — unlike <c>GateCondition</c>'s strict
/// XOR (exactly one required), neither set is valid here and means project-wide. Both set is rejected.
///
/// <see cref="SubFolders"/>/<see cref="Documents"/> stay real navigations (unlike every cross-module
/// reference in this module) because the parent/child relationship is entirely inside Documents' own
/// schema.
///
/// <see cref="MoveTo"/>'s cycle guard is new work — the backend has none (G7-02's correction 5): moving
/// a folder under its own descendant silently detaches a branch of the tree, since the
/// <c>ParentFolder</c> FK is <c>Restrict</c>, not a constraint that would catch it. The guard here is a
/// pure function over a supplied ancestor map (folder id → its own parent id), so it stays unit-testable
/// without a database — the caller (<c>Documents.Application</c>, G7-06) is responsible for loading that
/// map, the same "entity holds the pure state machine, Application does the relational lookup" split
/// <c>GateCondition</c>'s own remarks establish.
/// </summary>
internal sealed class DocumentFolder : Entity, IAuditable
{
    public string Name { get; private set; } = string.Empty;
    public Guid ProjectId { get; private set; }
    public Guid? ParentFolderId { get; private set; }
    public Guid? ProjectStageId { get; private set; }
    public Guid? ProjectTaskId { get; private set; }
    public bool IsDefault { get; private set; }

    private readonly List<DocumentFolder> _subFolders = [];
    public IReadOnlyCollection<DocumentFolder> SubFolders => _subFolders.AsReadOnly();

    private readonly List<ProjectDocument> _documents = [];
    public IReadOnlyCollection<ProjectDocument> Documents => _documents.AsReadOnly();

    private DocumentFolder()
    {
        // EF materialization.
    }

    /// <summary><paramref name="defaultAlreadyExistsForScope"/> is the caller's own pre-check (the
    /// partial-unique index <c>ux_document_folders_default_per_task</c>/<c>_per_stage</c> is the DB's
    /// last-resort backstop, G7-03) — passed in rather than queried here so this factory stays pure and
    /// unit-testable.</summary>
    public static Result<DocumentFolder> Create(
        Guid projectId, string name, Guid? parentFolderId = null,
        Guid? projectStageId = null, Guid? projectTaskId = null, bool isDefault = false,
        bool defaultAlreadyExistsForScope = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            return DocumentFolderErrors.NameRequired;

        if (projectStageId.HasValue && projectTaskId.HasValue)
            return DocumentFolderErrors.InvalidScope;

        if (isDefault && defaultAlreadyExistsForScope)
            return DocumentFolderErrors.DefaultAlreadyExists;

        return new DocumentFolder
        {
            ProjectId = projectId,
            Name = name,
            ParentFolderId = parentFolderId,
            ProjectStageId = projectStageId,
            ProjectTaskId = projectTaskId,
            IsDefault = isDefault,
        };
    }

    public Result Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return DocumentFolderErrors.NameRequired;

        Name = name;
        return Result.Success();
    }

    /// <summary>
    /// <paramref name="ancestorChain"/> maps every OTHER folder in the project to its own
    /// <c>ParentFolderId</c> (the caller loads this once per move, not per ancestor step). Rejects a
    /// self-move outright; otherwise walks up from <paramref name="newParentFolderId"/> and rejects if
    /// that walk ever reaches this folder's own id — the cycle a plain FK cannot catch.
    /// </summary>
    public Result MoveTo(Guid? newParentFolderId, IReadOnlyDictionary<Guid, Guid?> ancestorChain)
    {
        if (newParentFolderId is null)
        {
            ParentFolderId = null;
            return Result.Success();
        }

        if (newParentFolderId == Id)
            return DocumentFolderErrors.CyclicMove;

        var current = newParentFolderId;
        var steps = 0;
        while (current is { } folderId && ancestorChain.TryGetValue(folderId, out var parent))
        {
            if (parent == Id)
                return DocumentFolderErrors.CyclicMove;

            current = parent;

            // Defensive only: a well-formed tree can never need more steps than it has folders. Catches
            // a caller-supplied map that is itself already cyclic (a pre-existing data bug) rather than
            // looping forever.
            if (++steps > ancestorChain.Count)
                return DocumentFolderErrors.CyclicMove;
        }

        ParentFolderId = newParentFolderId;
        return Result.Success();
    }

    public void Deactivate() => SetActive(false);

    public void Activate() => SetActive(true);
}
