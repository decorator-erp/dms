using BuildingBlocks.Messaging;
using Crm.Contracts.Briefs;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Briefs;

internal sealed class GetCrmBriefSpacesByBriefQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmBriefSpacesByBriefQuery, IReadOnlyList<CrmBriefSpaceResponse>>
{
    public async Task<Result<IReadOnlyList<CrmBriefSpaceResponse>>> HandleAsync(
        GetCrmBriefSpacesByBriefQuery query, CancellationToken ct = default)
    {
        if (!await db.CrmDesignBriefs.AnyAsync(b => b.Id == query.BriefId, ct))
        {
            return Result.Failure<IReadOnlyList<CrmBriefSpaceResponse>>(Domain.CrmBriefErrors.NotFound);
        }

        List<Domain.CrmBriefSpace> entries = await db.CrmBriefSpaces.AsNoTracking()
            .Where(e => e.BriefId == query.BriefId)
            .OrderByDescending(e => e.CreatedAtUtc)
            .ToListAsync(ct);

        return BriefMapping.ToSpaceResponses(entries);
    }
}
