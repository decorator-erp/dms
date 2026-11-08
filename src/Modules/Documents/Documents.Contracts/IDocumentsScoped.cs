namespace Documents.Contracts;

/// <summary>
/// Marker for a Documents command or query whose project must be checked before the handler runs — the
/// Documents descendant of <c>Sites.Contracts.ISiteScoped</c> and <c>Crm.Contracts.ICrmProjectScoped</c>.
/// <c>DocumentsScopeDecorator</c> resolves the message to its project, then asks
/// <c>Projects.Contracts.IProjectAccessGuard</c>; a caller who cannot reach the project gets the same 404
/// as a missing row (CLAUDE.md: route claim 403 → module scope 404 → state 409). A command (any write)
/// also needs project level Editor or higher, read through <c>IProjectAccessLevelReader</c> — 403
/// <c>project_documents.InsufficientLevel</c> below that.
///
/// <para>A message implements exactly one of the five keyed markers below, named after the property the
/// project is resolved from. <c>DocumentsScopeCoverageTests</c> fails the build if a message carrying one
/// of those ids opts out, or carries the marker without the id.</para>
///
/// <para>The caller is read from <c>ICurrentUser</c> by the decorator rather than travelling on the message:
/// several of these messages carry no user id, and nothing outside the HTTP endpoints sends them.</para>
/// </summary>
public interface IDocumentsScoped;

/// <summary>Resolved directly: the message names its project.</summary>
public interface IDocumentsProjectScoped : IDocumentsScoped
{
    Guid ProjectId { get; }
}

/// <summary>Resolved through <c>IProjectDirectory.GetStageAsync</c>.</summary>
public interface IDocumentsStageScoped : IDocumentsScoped
{
    Guid ProjectStageId { get; }
}

/// <summary>Resolved through <c>IProjectDirectory.GetTaskAsync</c>.</summary>
public interface IDocumentsTaskScoped : IDocumentsScoped
{
    Guid ProjectTaskId { get; }
}

/// <summary>Resolved from the folder's own row (active folders only).</summary>
public interface IDocumentsFolderScoped : IDocumentsScoped
{
    Guid FolderId { get; }
}

/// <summary>Resolved from the document's own row (active documents only).</summary>
public interface IDocumentsDocumentScoped : IDocumentsScoped
{
    Guid DocumentId { get; }
}
