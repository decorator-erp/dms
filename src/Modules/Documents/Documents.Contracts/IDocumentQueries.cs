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
    /// Directly stage-scoped documents only — NOT the task-under-this-stage reach-through the backend's
    /// <c>ProjectDocumentRepository.GetByStageAsync</c> performs. That reach-through still exists (an
    /// <c>Attachment</c> gate on a stage must be satisfiable by a document uploaded to a task under it),
    /// but it's implemented on the GATE-SATISFACTION side instead (G7-07:
    /// <c>Projects.Infrastructure</c>'s <c>DocumentUploadedIntegrationEvent</c> handler already has
    /// direct access to <c>ProjectTask.ProjectStageId</c> and can resolve it in one step; this method
    /// would need a batched round-trip through <c>Projects.Contracts.IProjectDirectory</c> to do the
    /// same reach-through for a listing/UI concern this phase's verify block doesn't require).
    /// </summary>
    Task<IReadOnlyList<DocumentSummary>> ForStageAsync(Guid stageId, CancellationToken ct);

    Task<IReadOnlyList<DocumentSummary>> ForTaskAsync(Guid taskId, CancellationToken ct);
}
