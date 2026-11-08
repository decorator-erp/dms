namespace Crm.Application;

/// <summary>
/// <c>Crm.Application</c> legitimately references both <c>Crm.Contracts</c> and <c>Crm.Domain</c> —
/// those two can't reference each other (forced duplication, G9-01/G9-02→G9-06's own remarks) — so
/// every handler here needs to convert between them. <c>Contracts.X</c> and <c>Domain.X</c> are
/// guaranteed to share the same underlying values by construction (both ported from the same backend
/// enum, in the same order, in the same commit), so a numeric cast is correct — same deliberate,
/// disclosed trade-off <c>Projects.Application.ProjectsEnumMapping</c> makes for its own twelve pairs,
/// for the same reason: exhaustive hand-written switches for 25 pairs would cost more than this run's
/// remaining scope justifies. <see cref="EnumMappingParityTests"/> (in <c>Crm.Application.Tests</c>)
/// enumerates every value of every pair and asserts names and underlying values match exactly — the
/// same safety an exhaustive switch would give, for a fraction of the code.
///
/// <para>Two pairs are intentionally asymmetric in name: <c>Contracts.CrmApprovalState</c> ↔
/// <c>Domain.ApprovalState</c>, and <c>Contracts.RequirementPriority</c> ↔ <c>Domain.TaskPriority</c> —
/// both renamed on the Contracts side only, to avoid colliding with <c>Approvals.Contracts.ApprovalState</c>
/// and <c>Projects.Contracts.TaskPriority</c> respectively (see G9-01's own remarks). Values are
/// identical; only the name differs.</para>
/// </summary>
internal static class CrmEnumMapping
{
    public static Domain.PortalShareKind ToDomain(Contracts.PortalShareKind value) => (Domain.PortalShareKind)value;
    public static Contracts.PortalShareKind ToContracts(Domain.PortalShareKind value) => (Contracts.PortalShareKind)value;

    public static Domain.ContactRole ToDomain(Contracts.ContactRole value) => (Domain.ContactRole)value;
    public static Contracts.ContactRole ToContracts(Domain.ContactRole value) => (Contracts.ContactRole)value;

    public static Domain.LeadSource ToDomain(Contracts.LeadSource value) => (Domain.LeadSource)value;
    public static Contracts.LeadSource ToContracts(Domain.LeadSource value) => (Contracts.LeadSource)value;

    public static Domain.LeadStatus ToDomain(Contracts.LeadStatus value) => (Domain.LeadStatus)value;
    public static Contracts.LeadStatus ToContracts(Domain.LeadStatus value) => (Contracts.LeadStatus)value;

    public static Domain.MeetingStatus ToDomain(Contracts.MeetingStatus value) => (Domain.MeetingStatus)value;
    public static Contracts.MeetingStatus ToContracts(Domain.MeetingStatus value) => (Contracts.MeetingStatus)value;

    public static Domain.MeetingSlotStatus ToDomain(Contracts.MeetingSlotStatus value) => (Domain.MeetingSlotStatus)value;
    public static Contracts.MeetingSlotStatus ToContracts(Domain.MeetingSlotStatus value) => (Contracts.MeetingSlotStatus)value;

    public static Domain.MeetingType ToDomain(Contracts.MeetingType value) => (Domain.MeetingType)value;
    public static Contracts.MeetingType ToContracts(Domain.MeetingType value) => (Contracts.MeetingType)value;

    public static Domain.ApprovalState ToDomain(Contracts.CrmApprovalState value) => (Domain.ApprovalState)value;
    public static Contracts.CrmApprovalState ToContracts(Domain.ApprovalState value) => (Contracts.CrmApprovalState)value;

    public static Domain.TaskPriority ToDomain(Contracts.RequirementPriority value) => (Domain.TaskPriority)value;
    public static Contracts.RequirementPriority ToContracts(Domain.TaskPriority value) => (Contracts.RequirementPriority)value;

    public static Domain.ProjectSector ToDomain(Contracts.ProjectSector value) => (Domain.ProjectSector)value;
    public static Contracts.ProjectSector ToContracts(Domain.ProjectSector value) => (Contracts.ProjectSector)value;

    public static Domain.HiringReason ToDomain(Contracts.HiringReason value) => (Domain.HiringReason)value;
    public static Contracts.HiringReason ToContracts(Domain.HiringReason value) => (Contracts.HiringReason)value;

    public static Domain.PastExperience ToDomain(Contracts.PastExperience value) => (Domain.PastExperience)value;
    public static Contracts.PastExperience ToContracts(Domain.PastExperience value) => (Contracts.PastExperience)value;

    public static Domain.TimelinePressure ToDomain(Contracts.TimelinePressure value) => (Domain.TimelinePressure)value;
    public static Contracts.TimelinePressure ToContracts(Domain.TimelinePressure value) => (Contracts.TimelinePressure)value;

    public static Domain.UnitType ToDomain(Contracts.UnitType value) => (Domain.UnitType)value;
    public static Contracts.UnitType ToContracts(Domain.UnitType value) => (Contracts.UnitType)value;

    public static Domain.UnitCondition ToDomain(Contracts.UnitCondition value) => (Domain.UnitCondition)value;
    public static Contracts.UnitCondition ToContracts(Domain.UnitCondition value) => (Contracts.UnitCondition)value;

    public static Domain.UnitPosition ToDomain(Contracts.UnitPosition value) => (Domain.UnitPosition)value;
    public static Contracts.UnitPosition ToContracts(Domain.UnitPosition value) => (Contracts.UnitPosition)value;

    public static Domain.BuildingKind ToDomain(Contracts.BuildingKind value) => (Domain.BuildingKind)value;
    public static Contracts.BuildingKind ToContracts(Domain.BuildingKind value) => (Contracts.BuildingKind)value;

    public static Domain.ElevatorStatus ToDomain(Contracts.ElevatorStatus value) => (Domain.ElevatorStatus)value;
    public static Contracts.ElevatorStatus ToContracts(Domain.ElevatorStatus value) => (Contracts.ElevatorStatus)value;

    public static Domain.FurnitureStatus ToDomain(Contracts.FurnitureStatus value) => (Domain.FurnitureStatus)value;
    public static Contracts.FurnitureStatus ToContracts(Domain.FurnitureStatus value) => (Contracts.FurnitureStatus)value;

    public static Domain.FinishingPurpose ToDomain(Contracts.FinishingPurpose value) => (Domain.FinishingPurpose)value;
    public static Contracts.FinishingPurpose ToContracts(Domain.FinishingPurpose value) => (Contracts.FinishingPurpose)value;

    public static Domain.CatalogAttributeType ToDomain(Contracts.CatalogAttributeType value) => (Domain.CatalogAttributeType)value;
    public static Contracts.CatalogAttributeType ToContracts(Domain.CatalogAttributeType value) => (Contracts.CatalogAttributeType)value;

    public static Domain.CatalogSelectionMode ToDomain(Contracts.CatalogSelectionMode value) => (Domain.CatalogSelectionMode)value;
    public static Contracts.CatalogSelectionMode ToContracts(Domain.CatalogSelectionMode value) => (Contracts.CatalogSelectionMode)value;

    public static Domain.RequirementCategory ToDomain(Contracts.RequirementCategory value) => (Domain.RequirementCategory)value;
    public static Contracts.RequirementCategory ToContracts(Domain.RequirementCategory value) => (Contracts.RequirementCategory)value;

    public static Domain.RequirementPeriodStatus ToDomain(Contracts.RequirementPeriodStatus value) => (Domain.RequirementPeriodStatus)value;
    public static Contracts.RequirementPeriodStatus ToContracts(Domain.RequirementPeriodStatus value) => (Contracts.RequirementPeriodStatus)value;

    public static Domain.RequirementSource ToDomain(Contracts.RequirementSource value) => (Domain.RequirementSource)value;
    public static Contracts.RequirementSource ToContracts(Domain.RequirementSource value) => (Contracts.RequirementSource)value;

    public static Domain.CrmProvisionStatus ToDomain(Contracts.CrmProvisionStatus value) => (Domain.CrmProvisionStatus)value;
    public static Contracts.CrmProvisionStatus ToContracts(Domain.CrmProvisionStatus value) => (Contracts.CrmProvisionStatus)value;
}
