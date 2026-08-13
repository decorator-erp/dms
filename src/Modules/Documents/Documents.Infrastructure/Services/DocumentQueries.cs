using Documents.Contracts;
using Documents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Domain = Documents.Domain;

namespace Documents.Infrastructure.Services;

/// <summary>Implements <see cref="IDocumentQueries"/> over <see cref="DocumentsDbContext"/> — always via
/// a <c>Select</c> projection straight to <see cref="DocumentSummary"/>, never load-then-map, same
/// convention <c>HrDirectory</c>/<c>ProjectDirectory</c> established (G7-08). <c>ForStageAsync</c> is
/// deliberately direct-scope-only, no task-under-stage reach-through — see <see cref="IDocumentQueries"/>'s
/// own remarks for why that lives on the gate-satisfaction side instead.</summary>
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
            return [];

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

    private static System.Linq.Expressions.Expression<Func<Domain.ProjectDocument, DocumentSummary>> ToSummary() => d =>
        new DocumentSummary(
            d.Id, d.ProjectId, d.FolderId, d.ProjectStageId, d.ProjectTaskId, d.FileName, d.Version,
            d.RootDocumentId, d.IsLocked);
}
