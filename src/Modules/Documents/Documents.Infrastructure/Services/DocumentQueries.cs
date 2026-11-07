using Documents.Contracts;
using Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Domain = Documents.Domain;

namespace Documents.Infrastructure.Services;

/// <summary>Implements <see cref="IDocumentQueries"/> over <see cref="DocumentsDbContext"/> — always via
/// a <c>Select</c> projection straight to <see cref="DocumentSummary"/>, never load-then-map, same
/// convention <c>HrDirectory</c>/<c>ProjectDirectory</c> established (G7-08). <c>ForStageAsync</c>'s
/// single <c>ProjectStageId</c> predicate reaches through to task-scoped documents because the upload
/// path denormalizes the owning stage onto the row (E1-01) — no join, no cross-module call; see
/// <see cref="IDocumentQueries"/>'s own remarks.</summary>
internal sealed class DocumentQueries(DocumentsDbContext db) : IDocumentQueries
{
    public async Task<DocumentSummary?> GetAsync(Guid documentId, CancellationToken ct) =>
        await db.ProjectDocuments.AsNoTracking()
            .Where(d => d.Id == documentId && d.IsActive)
            .Select(ToSummary())
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<DocumentSummary>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.ProjectDocuments.AsNoTracking()
            .Where(d => ids.Contains(d.Id) && d.IsActive)
            .Select(ToSummary())
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DocumentSummary>> ForProjectAsync(Guid projectId, CancellationToken ct) =>
        await db.ProjectDocuments.AsNoTracking()
            .Where(d => d.ProjectId == projectId && d.IsActive)
            .Select(ToSummary())
            .ToListAsync(ct);

    public async Task<IReadOnlyList<DocumentSummary>> ForStageAsync(Guid stageId, CancellationToken ct) =>
        await db.ProjectDocuments.AsNoTracking()
            .Where(d => d.ProjectStageId == stageId && d.IsActive)
            .Select(ToSummary())
            .ToListAsync(ct);

    public async Task<IReadOnlyList<DocumentSummary>> ForTaskAsync(Guid taskId, CancellationToken ct) =>
        await db.ProjectDocuments.AsNoTracking()
            .Where(d => d.ProjectTaskId == taskId && d.IsActive)
            .Select(ToSummary())
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> StagesReferencedAsync(IReadOnlyCollection<Guid> projectStageIds, CancellationToken ct)
    {
        if (projectStageIds.Count == 0)
        {
            return [];
        }

        return await db.ProjectDocuments.AsNoTracking()
            .Where(d => d.ProjectStageId != null && projectStageIds.Contains(d.ProjectStageId.Value))
            .Select(d => d.ProjectStageId!.Value)
            .Union(db.DocumentFolders.AsNoTracking()
                .Where(f => f.ProjectStageId != null && projectStageIds.Contains(f.ProjectStageId.Value))
                .Select(f => f.ProjectStageId!.Value))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> TasksReferencedAsync(IReadOnlyCollection<Guid> projectTaskIds, CancellationToken ct)
    {
        if (projectTaskIds.Count == 0)
        {
            return [];
        }

        return await db.ProjectDocuments.AsNoTracking()
            .Where(d => d.ProjectTaskId != null && projectTaskIds.Contains(d.ProjectTaskId.Value))
            .Select(d => d.ProjectTaskId!.Value)
            .Union(db.DocumentFolders.AsNoTracking()
                .Where(f => f.ProjectTaskId != null && projectTaskIds.Contains(f.ProjectTaskId.Value))
                .Select(f => f.ProjectTaskId!.Value))
            .ToListAsync(ct);
    }

    private static System.Linq.Expressions.Expression<Func<Domain.ProjectDocument, DocumentSummary>> ToSummary() => d =>
        new DocumentSummary(
            d.Id, d.ProjectId, d.FolderId, d.ProjectStageId, d.ProjectTaskId, d.FileName, d.Version,
            d.RootDocumentId, d.IsLocked);
}
