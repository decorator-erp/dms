namespace Documents.Contracts;

/// <summary>
/// Cross-module read surface for Documents-owned data. Every lookup has a batch sibling — no
/// exceptions, per <c>docs/adding-a-module.md</c>'s Contracts rule. IDs only, no navigations —
/// consistent with every other module's own directory contract (<c>IHrDirectory</c>,
/// <c>IProjectDirectory</c>).
/// </summary>
public sealed record DocumentSummary(
    Guid Id,
    Guid ProjectId,
    Guid? FolderId,
    Guid? ProjectStageId,
    Guid? ProjectTaskId,
    string FileName,
    int Version,
    Guid RootDocumentId,
    bool IsLocked);

public interface IDocumentQueries
{
    Task<DocumentSummary?> GetAsync(Guid documentId, CancellationToken ct);
    Task<IReadOnlyList<DocumentSummary>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    Task<IReadOnlyList<DocumentSummary>> ForProjectAsync(Guid projectId, CancellationToken ct);

    /// <summary>
    /// Every document visible at this stage — those scoped to the stage directly AND those uploaded to a
    /// task under it, matching the backend's own <c>ProjectDocumentRepository.GetByStageAsync</c>
    /// (G7-02's correction 8).
    ///
    /// <para>The reach-through costs nothing at read time: a task-scoped upload denormalizes its owning
    /// stage onto <c>ProjectDocument.ProjectStageId</c> (E1-01), so this stays a single indexed
    /// predicate. An earlier revision of this comment argued the reach-through belonged on the
    /// gate-satisfaction side because doing it here "would need a batched round-trip through
    /// <c>IProjectDirectory</c>" — true of a query-time reach-through, but the write-time denormalization
    /// removes that trade-off entirely, and the argument had in any case been used to justify not
    /// implementing this half at all. Gate satisfaction ALSO resolves the stage itself, independently;
    /// the two are not alternatives.</para>
    /// </summary>
    Task<IReadOnlyList<DocumentSummary>> ForStageAsync(Guid stageId, CancellationToken ct);

    Task<IReadOnlyList<DocumentSummary>> ForTaskAsync(Guid taskId, CancellationToken ct);

    /// <summary>crm-complete FINDING-E-1: the subset of <paramref name="projectStageIds"/> that ANY document row
    /// (active or soft-deleted) or folder names. Deactivated rows still hold their FK to the stage, so Projects
    /// asks this before a hard delete instead of tripping the FK.</summary>
    Task<IReadOnlyList<Guid>> StagesReferencedAsync(IReadOnlyCollection<Guid> projectStageIds, CancellationToken ct);

    /// <summary>The task form of <see cref="StagesReferencedAsync"/>.</summary>
    Task<IReadOnlyList<Guid>> TasksReferencedAsync(IReadOnlyCollection<Guid> projectTaskIds, CancellationToken ct);

    /// <summary>P1: the project form of <see cref="StagesReferencedAsync"/> — the subset of
    /// <paramref name="projectIds"/> that any document row (active or soft-deleted) or folder names, asked before
    /// Projects hard-deletes a project.</summary>
    Task<IReadOnlyList<Guid>> ProjectsReferencedAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken ct);
}
