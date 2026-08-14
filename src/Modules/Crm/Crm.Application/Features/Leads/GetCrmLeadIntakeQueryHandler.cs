using BuildingBlocks.Messaging;
using Crm.Contracts.Leads;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Leads;

internal sealed class GetCrmLeadIntakeQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmLeadIntakeQuery, CrmLeadIntakeResponse>
{
    public async Task<Result<CrmLeadIntakeResponse>> HandleAsync(GetCrmLeadIntakeQuery query, CancellationToken ct = default)
    {
        var intake = await db.CrmLeadIntakes.AsNoTracking()
            .Include(i => i.Residential)
            .FirstOrDefaultAsync(i => i.LeadId == query.LeadId, ct);

        if (intake is null)
            return Result.Failure<CrmLeadIntakeResponse>(CrmLeadErrors.NotFound);

        var residential = intake.Residential is { } r
            ? new CrmLeadIntakeResidentialData(
                r.UnitType is { } ut ? CrmEnumMapping.ToContracts(ut) : null,
                r.UnitCondition is { } uc ? CrmEnumMapping.ToContracts(uc) : null,
                r.IsLicensed, r.IsRented,
                r.FinishingPurpose is { } fp ? CrmEnumMapping.ToContracts(fp) : null)
            : null;

        var data = new Contracts.Leads.CrmLeadIntakeData(
            intake.JobTitle, intake.ClientAddress,
            intake.Sector is { } sector ? CrmEnumMapping.ToContracts(sector) : null,
            intake.UnitAddress, intake.AreaSqm, intake.UsedDesignerBefore,
            intake.DesignerExperience is { } experience ? CrmEnumMapping.ToContracts(experience) : null,
            intake.DesignerExperienceReason, residential);

        return new CrmLeadIntakeResponse(query.LeadId, data);
    }
}
