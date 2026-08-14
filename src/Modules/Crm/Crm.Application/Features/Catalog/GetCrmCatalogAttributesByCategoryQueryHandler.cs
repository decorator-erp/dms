using BuildingBlocks.Messaging;
using Crm.Contracts.Catalog;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Catalog;

internal sealed class GetCrmCatalogAttributesByCategoryQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmCatalogAttributesByCategoryQuery, IReadOnlyList<CrmCatalogAttributeResponse>>
{
    public async Task<Result<IReadOnlyList<CrmCatalogAttributeResponse>>> HandleAsync(
        GetCrmCatalogAttributesByCategoryQuery query, CancellationToken ct = default)
    {
        List<CrmCatalogAttributeResponse> attributes = await db.CrmCatalogAttributes.AsNoTracking()
            .Where(a => a.CategoryId == query.CategoryId)
            .OrderBy(a => a.SortOrder)
            .Select(a => new CrmCatalogAttributeResponse(
                a.Id, a.CategoryId, a.Code, a.NameEn, a.NameAr, CrmEnumMapping.ToContracts(a.DataType),
                a.Unit, a.IsRequired, a.SortOrder, a.IsActive))
            .ToListAsync(ct);

        return attributes;
    }
}
