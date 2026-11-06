using BuildingBlocks.Messaging;
using Crm.Contracts.Catalog;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Catalog;

internal sealed class GetCrmCatalogTreeQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmCatalogTreeQuery, IReadOnlyList<CrmCatalogCategoryResponse>>
{
    public async Task<Result<IReadOnlyList<CrmCatalogCategoryResponse>>> HandleAsync(GetCrmCatalogTreeQuery query, CancellationToken ct = default)
    {
        IQueryable<CrmCatalogCategory> source = db.CrmCatalogCategories.AsNoTracking();
        if (query.SpaceTypeId is { } spaceTypeId)
        {
            // F4: a picker - active categories targeted to the space type directly or through an ancestor
            // (a mapping cascades to the subtree, G9-11). Paths are "/{rootId}/.../{ownId}/".
            List<string> targetedPaths = await db.CrmCatalogCategorySpaceTypes.AsNoTracking()
                .Where(m => m.SpaceTypeId == spaceTypeId)
                .Join(db.CrmCatalogCategories, m => m.CategoryId, c => c.Id, (m, c) => c.Path)
                .ToListAsync(ct);
            if (targetedPaths.Count == 0)
            {
                return new List<CrmCatalogCategoryResponse>();
            }

            // The catalog is a small admin tree: filter the active rows by path prefix in memory rather than
            // relying on a prefix-ANY translation.
            List<CrmCatalogCategoryResponse> picker = [.. (await source.Where(c => c.IsActive)
                    .OrderBy(c => c.Depth).ThenBy(c => c.SortOrder)
                    .ToListAsync(ct))
                .Where(c => targetedPaths.Exists(p => c.Path.StartsWith(p, StringComparison.Ordinal)))
                .Select(c => new CrmCatalogCategoryResponse(
                    c.Id, c.ParentId, c.Code, c.NameEn, c.NameAr, CrmEnumMapping.ToContracts(c.SelectionMode),
                    c.IsRequired, c.SortOrder, c.Path, c.Depth, c.IsActive))];
            return picker;
        }

        List<CrmCatalogCategoryResponse> categories = await source
            .OrderBy(c => c.Depth).ThenBy(c => c.SortOrder)
            .Select(c => new CrmCatalogCategoryResponse(
                c.Id, c.ParentId, c.Code, c.NameEn, c.NameAr, CrmEnumMapping.ToContracts(c.SelectionMode),
                c.IsRequired, c.SortOrder, c.Path, c.Depth, c.IsActive))
            .ToListAsync(ct);

        return categories;
    }
}
