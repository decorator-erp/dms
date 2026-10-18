using Crm.Domain;

namespace Crm.Application.Features.Briefs;

/// <summary>
/// <c>Crm.Contracts.Briefs.CrmBriefHeadData</c> and <c>Crm.Domain.CrmBriefHeadData</c> share a name —
/// both ported from the same backend record, same reasoning as <c>CrmLeadIntakeData</c>'s pair (G9-09's
/// remarks) — so this maps between them explicitly, in one place, rather than repeating the
/// field-by-field conversion (and its ambiguous-name workaround) across every Brief handler that
/// touches the head.
/// </summary>
internal static class BriefMapping
{
    public static Domain.CrmBriefHeadData ToDomain(Contracts.Briefs.CrmBriefHeadData d) => new(
        CrmEnumMapping.ToDomain(d.UnitType),
        CrmEnumMapping.ToDomain(d.UnitCondition),
        d.Address, d.FloorNo, d.UnitsPerFloor, d.AreaSqm,
        d.BuildingKind is { } bk ? CrmEnumMapping.ToDomain(bk) : null,
        d.IsLicensed,
        d.Position is { } p ? CrmEnumMapping.ToDomain(p) : null,
        d.ElevatorStatus is { } es ? CrmEnumMapping.ToDomain(es) : null,
        d.ReceivesFrequentVisitors, d.VisitorsNote, d.UsedDesignerBefore,
        d.DesignerExperience is { } de ? CrmEnumMapping.ToDomain(de) : null,
        d.DesignerExperienceReason, CrmEnumMapping.ToDomain(d.HiringReasons),
        d.PreferredStyle, d.PreferredColors,
        d.FurnitureStatus is { } fs ? CrmEnumMapping.ToDomain(fs) : null,
        d.ExpectedBudget,
        d.TimelinePressure is { } tp ? CrmEnumMapping.ToDomain(tp) : null,
        d.ArchitecturalRequests, d.OtherNeeds, d.IsRented,
        d.FinishingPurpose is { } fp ? CrmEnumMapping.ToDomain(fp) : null);

    public static Contracts.Briefs.CrmDesignBriefResponse ToResponse(CrmDesignBrief brief)
    {
        var data = new Contracts.Briefs.CrmBriefHeadData(
            CrmEnumMapping.ToContracts(brief.UnitType), CrmEnumMapping.ToContracts(brief.UnitCondition),
            brief.Address, brief.FloorNo, brief.UnitsPerFloor, brief.AreaSqm,
            brief.BuildingKind is { } bk ? CrmEnumMapping.ToContracts(bk) : null, brief.IsLicensed,
            brief.Position is { } p ? CrmEnumMapping.ToContracts(p) : null,
            brief.ElevatorStatus is { } es ? CrmEnumMapping.ToContracts(es) : null,
            brief.ReceivesFrequentVisitors, brief.VisitorsNote, brief.UsedDesignerBefore,
            brief.DesignerExperience is { } de ? CrmEnumMapping.ToContracts(de) : null,
            brief.DesignerExperienceReason, CrmEnumMapping.ToContracts(brief.HiringReasons),
            brief.PreferredStyle, brief.PreferredColors,
            brief.FurnitureStatus is { } fs ? CrmEnumMapping.ToContracts(fs) : null, brief.ExpectedBudget,
            brief.TimelinePressure is { } tp ? CrmEnumMapping.ToContracts(tp) : null,
            brief.ArchitecturalRequests, brief.OtherNeeds, brief.IsRented,
            brief.FinishingPurpose is { } fp ? CrmEnumMapping.ToContracts(fp) : null);

        return new Contracts.Briefs.CrmDesignBriefResponse(
            brief.Id, brief.ProjectId, brief.ProjectStageId, brief.CreatedById, brief.LastMeetingId,
            CrmEnumMapping.ToContracts(brief.Status), data);
    }
}
