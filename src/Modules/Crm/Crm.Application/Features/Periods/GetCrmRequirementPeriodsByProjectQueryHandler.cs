using BuildingBlocks.Messaging;
using Crm.Contracts.Periods;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Periods;

internal sealed class GetCrmRequirementPeriodsByProjectQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmRequirementPeriodsByProjectQuery, IReadOnlyList<CrmRequirementPeriodResponse>>
{
    public async Task<Result<IReadOnlyList<CrmRequirementPeriodResponse>>> HandleAsync(
        GetCrmRequirementPeriodsByProjectQuery query, CancellationToken ct = default)
    {
        List<CrmRequirementPeriodResponse> periods = await db.CrmRequirementPeriods.AsNoTracking()
            .Where(p => p.ProjectId == query.ProjectId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Select(p => new CrmRequirementPeriodResponse(
                p.Id, p.ProjectId, p.ProjectStageId, CrmEnumMapping.ToContracts(p.Status), p.ClosesAtUtc,
                p.ClosedAtUtc, p.OpenedById))
            .ToListAsync(ct);

        return periods;
    }
}
