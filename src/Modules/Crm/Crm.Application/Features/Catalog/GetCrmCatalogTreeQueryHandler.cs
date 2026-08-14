using BuildingBlocks.Messaging;
using Crm.Contracts.Catalog;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Catalog;

internal sealed class GetCrmCatalogTreeQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmCatalogTreeQuery, IReadOnlyList<CrmCatalogCategoryResponse>>
{
    public async Task<Result<IReadOnlyList<CrmCatalogCategoryResponse>>> HandleAsync(GetCrmCatalogTreeQuery query, CancellationToken ct = default)
    {
        List<CrmCatalogCategoryResponse> categories = await db.CrmCatalogCategories.AsNoTracking()
            .OrderBy(c => c.Depth).ThenBy(c => c.SortOrder)
            .Select(c => new CrmCatalogCategoryResponse(
                c.Id, c.ParentId, c.Code, c.NameEn, c.NameAr, CrmEnumMapping.ToContracts(c.SelectionMode),
                c.IsRequired, c.SortOrder, c.Path, c.Depth, c.IsActive))
            .ToListAsync(ct);

        return categories;
    }
}
