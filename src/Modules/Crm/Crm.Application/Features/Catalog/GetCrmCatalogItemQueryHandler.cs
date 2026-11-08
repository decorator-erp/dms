using BuildingBlocks.Messaging;
using Crm.Contracts.Catalog;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Catalog;

/// <summary>S10: one catalog item and its attribute values — two statements, whatever the value count.</summary>
internal sealed class GetCrmCatalogItemQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmCatalogItemQuery, CrmCatalogItemDetailResponse>
{
    public async Task<Result<CrmCatalogItemDetailResponse>> HandleAsync(GetCrmCatalogItemQuery query, CancellationToken ct = default)
    {
        CrmCatalogItem? item = await db.CrmCatalogItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == query.ItemId, ct);
        if (item is null)
        {
            return Result.Failure<CrmCatalogItemDetailResponse>(CrmCatalogErrors.ItemNotFound);
        }

        List<CatalogAttributeValueData> values = await db.CrmCatalogItemAttributeValues.AsNoTracking()
            .Where(v => v.ItemId == query.ItemId)
            .OrderBy(v => v.AttributeId)
            .Select(v => new CatalogAttributeValueData(v.AttributeId, v.ValueText, v.ValueNumber, v.ValueBool, v.ValueDate, v.OptionId))
            .ToListAsync(ct);

        return new CrmCatalogItemDetailResponse(
            item.Id, item.CategoryId, item.Code, item.NameEn, item.NameAr, item.IsPurchasable, item.SortOrder, item.IsActive,
            item.DescriptionEn, item.DescriptionAr, item.ImageUri, values);
    }
}
