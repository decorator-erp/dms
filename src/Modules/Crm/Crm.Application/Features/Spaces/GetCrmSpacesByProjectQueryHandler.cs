using BuildingBlocks.Messaging;
using Crm.Contracts.Spaces;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Spaces;

internal sealed class GetCrmSpacesByProjectQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmSpacesByProjectQuery, IReadOnlyList<CrmSpaceResponse>>
{
    public async Task<Result<IReadOnlyList<CrmSpaceResponse>>> HandleAsync(GetCrmSpacesByProjectQuery query, CancellationToken ct = default)
    {
        List<CrmSpaceResponse> spaces = await db.CrmSpaces.AsNoTracking()
            .Where(s => s.ProjectId == query.ProjectId)
            .OrderBy(s => s.SortOrder)
            .Select(s => new CrmSpaceResponse(s.Id, s.ProjectId, s.SpaceTypeId, s.Name, s.Description, s.SortOrder, s.IsActive))
            .ToListAsync(ct);

        return spaces;
    }
}
