using BuildingBlocks.Messaging;

namespace Documents.Contracts.Folders;

/// <summary>See the remarks on <c>Documents.Contracts.Documents.DocumentContracts</c> — same reason
/// these command/query wrapper types and DTOs live here rather than in
/// <c>Documents.Application</c>.</summary>
public sealed record DocumentFolderResponse(
    Guid Id,
    string Name,
    Guid ProjectId,
    Guid? ParentFolderId,
    Guid? ProjectStageId,
    Guid? ProjectTaskId,
    bool IsDefault);

/// <summary>Exactly one of <paramref name="ProjectStageId"/>/<paramref name="ProjectTaskId"/> may be
/// set (validated in the handler, G7-06) — project-wide when neither is. There is no folder-scope
/// enum; scope is the same three-way XOR-or-neither the entity itself enforces.</summary>
public sealed record CreateDocumentFolderCommand(
    Guid ProjectId,
    string Name,
    Guid? ParentFolderId = null,
    Guid? ProjectStageId = null,
    Guid? ProjectTaskId = null) : ICommand<Guid>, IDocumentsProjectScoped;

public sealed record CreateStageFolderCommand(Guid ProjectStageId, string Name, Guid? ParentFolderId = null)
    : ICommand<Guid>, IDocumentsStageScoped;

public sealed record CreateTaskFolderCommand(Guid ProjectTaskId, string Name, Guid? ParentFolderId = null)
    : ICommand<Guid>, IDocumentsTaskScoped;

public sealed record DeleteDocumentFolderCommand(Guid FolderId) : ICommand, IDocumentsFolderScoped;

/// <summary>Rejected on a self-move, a cycle (moving a folder under its own descendant — new work at
/// G7-02/G7-06, the backend has no such guard), or a same-project-sibling name collision. Scope
/// (project/stage/task) and <c>IsDefault</c> are NOT re-validated on move — same as the backend, ported
/// deliberately rather than silently "fixed," since neither gap was in this phase's scope to close.</summary>
public sealed record MoveDocumentFolderCommand(Guid FolderId, Guid? NewParentFolderId) : ICommand, IDocumentsFolderScoped;

public sealed record RenameDocumentFolderCommand(Guid FolderId, string Name) : ICommand, IDocumentsFolderScoped;

public sealed record GetDocumentFoldersByProjectQuery(Guid ProjectId) : IQuery<IReadOnlyList<DocumentFolderResponse>>, IDocumentsProjectScoped;

public sealed record GetDocumentFoldersByStageQuery(Guid ProjectStageId)
    : IQuery<IReadOnlyList<DocumentFolderResponse>>, IDocumentsStageScoped;

public sealed record GetDocumentFoldersByTaskQuery(Guid ProjectTaskId)
    : IQuery<IReadOnlyList<DocumentFolderResponse>>, IDocumentsTaskScoped;

public sealed record GetDocumentFolderChildrenQuery(Guid FolderId) : IQuery<IReadOnlyList<DocumentFolderResponse>>, IDocumentsFolderScoped;
