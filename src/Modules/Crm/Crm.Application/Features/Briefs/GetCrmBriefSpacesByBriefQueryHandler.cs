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
        List<CrmBriefSpaceResponse> entries = await db.CrmBriefSpaces.AsNoTracking()
            .Where(e => e.BriefId == query.BriefId)
            .OrderByDescending(e => e.CreatedAtUtc)
            .Select(e => new CrmBriefSpaceResponse(
                e.Id, e.BriefId, e.SpaceId, e.CountOrPieces, e.Requirements, e.MeetingId,
                CrmEnumMapping.ToContracts(e.ApprovalStatus), e.SupersedesSpaceId, e.ProjectTaskId, e.CreatedById))
            .ToListAsync(ct);

        return entries;
    }
}
