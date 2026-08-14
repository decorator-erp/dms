using BuildingBlocks.Messaging;
using Crm.Contracts.Periods;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Periods;

internal sealed class GetCrmRequirementPeriodByIdQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmRequirementPeriodByIdQuery, CrmRequirementPeriodResponse>
{
    internal static Func<CrmRequirementPeriod, CrmRequirementPeriodResponse> ToResponse() => p =>
        new CrmRequirementPeriodResponse(
            p.Id, p.ProjectId, p.ProjectStageId, CrmEnumMapping.ToContracts(p.Status), p.ClosesAtUtc,
            p.ClosedAtUtc, p.OpenedById);

    public async Task<Result<CrmRequirementPeriodResponse>> HandleAsync(GetCrmRequirementPeriodByIdQuery query, CancellationToken ct = default)
    {
        var period = await db.CrmRequirementPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == query.PeriodId, ct);
        if (period is null)
            return Result.Failure<CrmRequirementPeriodResponse>(CrmPeriodErrors.NotFound);

        return ToResponse()(period);
    }
}
