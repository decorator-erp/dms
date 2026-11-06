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
            // FINDING-F-2: an inactive category hides its whole subtree, and an inactive mapped category targets
            // nothing.
            List<CrmCatalogCategory> all = await source.ToListAsync(ct);
            HashSet<string> inactive = [.. all.Where(c => !c.IsActive).Select(c => $"/{c.Id}/")];
            List<Guid> mapped = await db.CrmCatalogCategorySpaceTypes.AsNoTracking()
                .Where(m => m.SpaceTypeId == spaceTypeId).Select(m => m.CategoryId).ToListAsync(ct);
            List<string> targetedPaths = [.. all.Where(c => mapped.Contains(c.Id) && c.IsActive).Select(c => c.Path)];
            List<CrmCatalogCategoryResponse> picker = [.. all
                .Where(c => targetedPaths.Exists(p => c.Path.StartsWith(p, StringComparison.Ordinal))
                    && !inactive.Any(seg => c.Path.Contains(seg, StringComparison.Ordinal)))
                .OrderBy(c => c.Depth).ThenBy(c => c.SortOrder)
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
