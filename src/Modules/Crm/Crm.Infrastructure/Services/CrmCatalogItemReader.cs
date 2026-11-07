using Crm.Contracts;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crm.Infrastructure.Services;

/// <summary><see cref="ICrmCatalogItemReader"/>'s implementation — <see cref="CrmSpaceReader"/>'s shape:
/// projects straight to the Contracts DTO and does NOT filter on <c>IsActive</c>, projecting the flag
/// instead.</summary>
internal sealed class CrmCatalogItemReader(CrmDbContext db) : ICrmCatalogItemReader
{
    public Task<CrmCatalogItemSummary?> GetAsync(Guid catalogItemId, CancellationToken ct = default) =>
        db.CrmCatalogItems.AsNoTracking()
            .Where(i => i.Id == catalogItemId)
            .Select(i => new CrmCatalogItemSummary(i.Id, i.CategoryId, i.Code, i.IsActive))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<CrmCatalogItemSummary>> GetManyAsync(
        IReadOnlyCollection<Guid> catalogItemIds, CancellationToken ct = default)
    {
        if (catalogItemIds.Count == 0)
        {
            return [];
        }

        return await db.CrmCatalogItems.AsNoTracking()
            .Where(i => catalogItemIds.Contains(i.Id))
            .Select(i => new CrmCatalogItemSummary(i.Id, i.CategoryId, i.Code, i.IsActive))
            .ToListAsync(ct);
    }
}
