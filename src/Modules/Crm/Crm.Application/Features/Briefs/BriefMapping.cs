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
}
