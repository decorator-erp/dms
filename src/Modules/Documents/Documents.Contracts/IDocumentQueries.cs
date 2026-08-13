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

    /// <summary>Reach-through: includes documents attached to a task under this stage, not just
    /// directly stage-scoped rows — matches the backend's own
    /// <c>ProjectDocumentRepository.GetByStageAsync</c>/<c>ExistsForStageAsync</c>. An <c>Attachment</c>
    /// gate on a stage must be satisfiable by a document uploaded to a task under it, or the gate can
    /// never resolve for the (common) case where the deliverable was actually attached at the task
    /// level.</summary>
    Task<IReadOnlyList<DocumentSummary>> ForStageAsync(Guid stageId, CancellationToken ct);

    Task<IReadOnlyList<DocumentSummary>> ForTaskAsync(Guid taskId, CancellationToken ct);
}
