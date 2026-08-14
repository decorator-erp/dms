using BuildingBlocks.Messaging;
using Crm.Contracts.Catalog;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Catalog;

internal sealed class GetCrmCatalogItemsByCategoryQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmCatalogItemsByCategoryQuery, IReadOnlyList<CrmCatalogItemResponse>>
{
    public async Task<Result<IReadOnlyList<CrmCatalogItemResponse>>> HandleAsync(
        GetCrmCatalogItemsByCategoryQuery query, CancellationToken ct = default)
    {
        List<CrmCatalogItemResponse> items = await db.CrmCatalogItems.AsNoTracking()
            .Where(i => i.CategoryId == query.CategoryId)
            .OrderBy(i => i.SortOrder)
            .Select(i => new CrmCatalogItemResponse(
                i.Id, i.CategoryId, i.Code, i.NameEn, i.NameAr, i.IsPurchasable, i.SortOrder, i.IsActive))
            .ToListAsync(ct);

        return items;
    }
}
