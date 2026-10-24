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
        // No .Include(i => i.Residential) - CrmLeadIntakeConfiguration.Ignore(i => i.Residential)
        // means EF has no navigation to traverse there; fetched as a separate query below instead.
        CrmLeadIntake? intake = await db.CrmLeadIntakes.AsNoTracking()
            .FirstOrDefaultAsync(i => i.LeadId == query.LeadId, ct);

        if (intake is null)
        {
            // "No lead" is a 404; "a lead with no intake yet" is an empty intake, since the intake is
            // created lazily on first save.
            bool leadExists = await db.CrmLeads.AnyAsync(l => l.Id == query.LeadId, ct);
            return leadExists
                ? new CrmLeadIntakeResponse(query.LeadId, new Contracts.Leads.CrmLeadIntakeData())
                : Result.Failure<CrmLeadIntakeResponse>(CrmLeadErrors.NotFound);
        }

        CrmLeadIntakeResidential? residentialRow = await db.CrmLeadIntakeResidentials.AsNoTracking()
            .FirstOrDefaultAsync(r => r.LeadIntakeId == intake.Id, ct);

        CrmLeadIntakeResidentialData? residential = residentialRow is { } r
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
