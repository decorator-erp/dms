using BuildingBlocks.Messaging;
using Crm.Contracts.Spaces;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Spaces;

internal sealed class GetCrmSpaceTypesQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmSpaceTypesQuery, IReadOnlyList<CrmSpaceTypeResponse>>
{
    public async Task<Result<IReadOnlyList<CrmSpaceTypeResponse>>> HandleAsync(GetCrmSpaceTypesQuery query, CancellationToken ct = default)
    {
        List<CrmSpaceTypeResponse> types = await db.CrmSpaceTypes.AsNoTracking()
            .OrderBy(t => t.SortOrder)
            .Select(t => new CrmSpaceTypeResponse(t.Id, t.Code, t.NameEn, t.NameAr, t.SortOrder, t.IsActive))
            .ToListAsync(ct);

        return types;
    }
}
