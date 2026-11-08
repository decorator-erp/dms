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
internal sealed class DocumentQueries(DocumentsDbContext db, IFileStorageService fileStorage) : IDocumentQueries
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

    public async Task<IReadOnlyList<Guid>> ProjectsReferencedAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken ct)
    {
        if (projectIds.Count == 0)
        {
            return [];
        }

        return await db.ProjectDocuments.AsNoTracking()
            .Where(d => projectIds.Contains(d.ProjectId))
            .Select(d => d.ProjectId)
            .Union(db.DocumentFolders.AsNoTracking()
                .Where(f => projectIds.Contains(f.ProjectId))
                .Select(f => f.ProjectId))
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

    public async Task<DocumentFileSummary?> GetFileAsync(Guid documentId, CancellationToken ct) =>
        await GetFilesAsync([documentId], ct) is [var first, ..] ? first : null;

    public async Task<IReadOnlyList<DocumentFileSummary>> GetFilesAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken ct)
    {
        if (documentIds.Count == 0)
        {
            return [];
        }

        return await db.ProjectDocuments.AsNoTracking()
            .Where(d => documentIds.Contains(d.Id) && d.IsActive)
            .Select(ToFileSummary())
            .ToListAsync(ct);
    }

    public async Task<DocumentFileSummary?> GetLatestFileAsync(Guid rootDocumentId, CancellationToken ct) =>
        await GetLatestFilesAsync([rootDocumentId], ct) is [var first, ..] ? first : null;

    public async Task<IReadOnlyList<DocumentFileSummary>> GetLatestFilesAsync(IReadOnlyCollection<Guid> rootDocumentIds, CancellationToken ct)
    {
        if (rootDocumentIds.Count == 0)
        {
            return [];
        }

        // A lineage is a handful of rows, so the newest per root is picked in memory rather than with a window
        // function the provider may or may not translate.
        List<DocumentFileSummary> active = await db.ProjectDocuments.AsNoTracking()
            .Where(d => rootDocumentIds.Contains(d.RootDocumentId) && d.IsActive)
            .Select(ToFileSummary())
            .ToListAsync(ct);
        return [.. active.GroupBy(d => d.RootDocumentId).Select(g => g.MaxBy(d => d.Version)!)];
    }

    public async Task<DocumentContent?> OpenContentAsync(Guid documentId, CancellationToken ct) =>
        await OpenContentsAsync([documentId], ct) is [var first, ..] ? first : null;

    public async Task<IReadOnlyList<DocumentContent>> OpenContentsAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken ct)
    {
        if (documentIds.Count == 0)
        {
            return [];
        }

        var rows = await db.ProjectDocuments.AsNoTracking()
            .Where(d => documentIds.Contains(d.Id) && d.IsActive)
            .Select(d => new { d.Id, d.FileName, d.ContentType, d.StoragePath })
            .ToListAsync(ct);

        List<DocumentContent> opened = [];
        foreach (var row in rows)
        {
            Stream? content = await fileStorage.OpenReadAsync(row.StoragePath, ct);
            if (content is not null)
            {
                opened.Add(new DocumentContent(row.Id, row.FileName, row.ContentType, content));
            }
        }

        return opened;
    }

    private static System.Linq.Expressions.Expression<Func<Domain.ProjectDocument, DocumentFileSummary>> ToFileSummary() => d =>
        new DocumentFileSummary(
            d.Id, d.ProjectId, d.RootDocumentId, d.Version, d.FileName, d.ContentType, d.FileSizeBytes, d.UploadedOnUtc);

    private static System.Linq.Expressions.Expression<Func<Domain.ProjectDocument, DocumentSummary>> ToSummary() => d =>
        new DocumentSummary(
            d.Id, d.ProjectId, d.FolderId, d.ProjectStageId, d.ProjectTaskId, d.FileName, d.Version,
            d.RootDocumentId, d.IsLocked);
}
