using Crm.Contracts.Leads;
using Crm.Domain;
using DomainIntakeData = Crm.Domain.CrmLeadIntakeData;

namespace Crm.Application.Features.Leads;

/// <summary>The intake's contract-to-domain mapping, shared by the office save
/// (<see cref="SaveCrmLeadIntakeCommandHandler"/>) and the public form's submit
/// (<see cref="SubmitPublicLeadCommandHandler"/>), so both store the same answers the same way.</summary>
internal static class LeadIntakeMapping
{
    public static DomainIntakeData ToDomain(Contracts.Leads.CrmLeadIntakeData data) => new(
        data.JobTitle, data.ClientAddress,
        data.Sector is { } sector ? CrmEnumMapping.ToDomain(sector) : null,
        data.UnitAddress, data.AreaSqm, data.UsedDesignerBefore,
        data.DesignerExperience is { } experience ? CrmEnumMapping.ToDomain(experience) : null,
        data.DesignerExperienceReason);

    public static ResidentialIntakeData? ToDomain(CrmLeadIntakeResidentialData? r) => r is null
        ? null
        : new ResidentialIntakeData(
            r.UnitType is { } unitType ? CrmEnumMapping.ToDomain(unitType) : null,
            r.UnitCondition is { } unitCondition ? CrmEnumMapping.ToDomain(unitCondition) : null,
            r.IsLicensed, r.IsRented,
            r.FinishingPurpose is { } purpose ? CrmEnumMapping.ToDomain(purpose) : null);
}
