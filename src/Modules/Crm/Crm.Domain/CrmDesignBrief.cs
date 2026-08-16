using SharedKernel;

namespace Crm.Domain;

/// <summary>All editable head fields in one carrier, ported from the backend's <c>CrmBriefHeadData</c>.
/// The entity applies them atomically.</summary>
internal sealed record CrmBriefHeadData(
    UnitType UnitType,
    UnitCondition UnitCondition,
    string? Address = null,
    int? FloorNo = null,
    int? UnitsPerFloor = null,
    decimal? AreaSqm = null,
    BuildingKind? BuildingKind = null,
    bool? IsLicensed = null,
    UnitPosition? Position = null,
    ElevatorStatus? ElevatorStatus = null,
    bool? ReceivesFrequentVisitors = null,
    string? VisitorsNote = null,
    bool? UsedDesignerBefore = null,
    PastExperience? DesignerExperience = null,
    string? DesignerExperienceReason = null,
    HiringReason HiringReasons = HiringReason.None,
    string? PreferredStyle = null,
    string? PreferredColors = null,
    FurnitureStatus? FurnitureStatus = null,
    decimal? ExpectedBudget = null,
    TimelinePressure? TimelinePressure = null,
    string? ArchitecturalRequests = null,
    string? OtherNeeds = null,
    bool? IsRented = null,
    FinishingPurpose? FinishingPurpose = null);

/// <summary>
/// Ported from <c>../backend/Domain/Entities/CrmDesignBrief.cs</c> — the digitized "Application Form"
/// head, a true 1:1 with a project stage, edited in place. Any edit returns the status to
/// <see cref="ApprovalState.PendingApproval"/>; the approval attempts form the trail. The append-only
/// ledger lives one level down in <see cref="CrmBriefSpace"/> — the head is exempt from the ledger
/// rule.
///
/// <para><b>There is deliberately no <c>SiteId</c> here.</b> G9-04 added one on the instruction of
/// <c>PLAN-PHASE4.md</c>, which was an error in that plan rather than a design choice: the backend
/// entity has no such field, and the real brief↔site link runs the OTHER way, as
/// <c>Site.SourceBriefId</c> (<c>../backend/Domain/Entities/Site.cs:57-66</c>, documented there as
/// "provenance, not a dependency… written only by <c>BackfillSitesFromBriefs</c>; there is
/// deliberately no domain setter"). Nothing ever wrote the column. It was dropped at S1-00, before
/// Sites introduced the real link, so the model never carries two fields for one relationship
/// pointing in opposite directions.</para>
/// </summary>
internal sealed class CrmDesignBrief : Entity, IAuditable
{
    public Guid ProjectId { get; private set; }
    public Guid? ProjectStageId { get; private set; }
    public Guid CreatedById { get; private set; }
    public Guid? LastMeetingId { get; private set; }
    public ApprovalState Status { get; private set; }

    public UnitType UnitType { get; private set; }
    public UnitCondition UnitCondition { get; private set; }
    public string? Address { get; private set; }
    public int? FloorNo { get; private set; }
    public int? UnitsPerFloor { get; private set; }
    public decimal? AreaSqm { get; private set; }
    public BuildingKind? BuildingKind { get; private set; }
    public bool? IsLicensed { get; private set; }
    public UnitPosition? Position { get; private set; }
    public ElevatorStatus? ElevatorStatus { get; private set; }
    public bool? ReceivesFrequentVisitors { get; private set; }
    public string? VisitorsNote { get; private set; }
    public bool? UsedDesignerBefore { get; private set; }
    public PastExperience? DesignerExperience { get; private set; }
    public string? DesignerExperienceReason { get; private set; }
    public HiringReason HiringReasons { get; private set; }
    public string? PreferredStyle { get; private set; }
    public string? PreferredColors { get; private set; }
    public FurnitureStatus? FurnitureStatus { get; private set; }

    /// <summary>Office-side only — never exposed to designers (excluded from
    /// <c>GetClientDesignProfileQuery</c>'s response).</summary>
    public decimal? ExpectedBudget { get; private set; }

    public TimelinePressure? TimelinePressure { get; private set; }
    public string? ArchitecturalRequests { get; private set; }
    public string? OtherNeeds { get; private set; }

    /// <summary>Stamped from the intake at seeding; never edited via the head form.</summary>
    public ProjectSector? Sector { get; private set; }

    public bool? IsRented { get; private set; }
    public FinishingPurpose? FinishingPurpose { get; private set; }

    private CrmDesignBrief() { }

    public static CrmDesignBrief Create(
        Guid projectId, Guid createdById, Guid? projectStageId, CrmBriefHeadData data)
    {
        var brief = new CrmDesignBrief
        {
            ProjectId = projectId,
            CreatedById = createdById,
            ProjectStageId = projectStageId,
            Status = ApprovalState.Draft,
        };
        brief.Apply(data);
        return brief;
    }

    /// <summary>Edit-in-place — always allowed, the head is exempt from the ledger rule. Any edit to a
    /// non-Draft head returns it to <see cref="ApprovalState.PendingApproval"/> so it must be
    /// re-confirmed.</summary>
    public Result UpdateHead(CrmBriefHeadData data, Guid? lastMeetingId)
    {
        Apply(data);
        if (lastMeetingId is not null)
            LastMeetingId = lastMeetingId;

        if (Status != ApprovalState.Draft)
            Status = ApprovalState.PendingApproval;

        return Result.Success();
    }

    internal Result MarkPendingApproval()
    {
        if (Status == ApprovalState.PendingApproval)
            return Result.Success();

        Status = ApprovalState.PendingApproval;
        return Result.Success();
    }

    internal Result MarkApproved()
    {
        if (Status != ApprovalState.PendingApproval)
            return CrmBriefErrors.NotPending;

        Status = ApprovalState.Approved;
        return Result.Success();
    }

    internal Result MarkRejected()
    {
        if (Status != ApprovalState.PendingApproval)
            return CrmBriefErrors.NotPending;

        Status = ApprovalState.Rejected;
        return Result.Success();
    }

    /// <summary>Hop 2 of lead conversion — copy-once from the converted lead's intake into a freshly
    /// seeded Draft head. Guard: only while <see cref="Status"/> is Draft; never re-applied.</summary>
    internal Result SeedFromIntake(
        ProjectSector? sector, string? unitAddress, decimal? areaSqm, bool? usedDesignerBefore,
        PastExperience? designerExperience, string? designerExperienceReason,
        UnitType? residentialUnitType, UnitCondition? residentialUnitCondition,
        bool? residentialIsLicensed, bool? residentialIsRented, FinishingPurpose? residentialFinishingPurpose)
    {
        if (Status != ApprovalState.Draft) return CrmBriefErrors.NotDraft;

        Sector = sector;
        Address ??= unitAddress;
        AreaSqm ??= areaSqm;
        UsedDesignerBefore ??= usedDesignerBefore;
        DesignerExperience ??= designerExperience;
        DesignerExperienceReason ??= designerExperienceReason;

        if (residentialUnitType is not null) UnitType = residentialUnitType.Value;
        if (residentialUnitCondition is not null) UnitCondition = residentialUnitCondition.Value;
        IsLicensed ??= residentialIsLicensed;
        IsRented ??= residentialIsRented;
        FinishingPurpose ??= residentialFinishingPurpose;

        return Result.Success();
    }

    private void Apply(CrmBriefHeadData d)
    {
        UnitType = d.UnitType;
        UnitCondition = d.UnitCondition;
        Address = d.Address;
        FloorNo = d.FloorNo;
        UnitsPerFloor = d.UnitsPerFloor;
        AreaSqm = d.AreaSqm;
        BuildingKind = d.BuildingKind;
        IsLicensed = d.IsLicensed;
        Position = d.Position;
        ElevatorStatus = d.ElevatorStatus;
        ReceivesFrequentVisitors = d.ReceivesFrequentVisitors;
        VisitorsNote = d.VisitorsNote;
        UsedDesignerBefore = d.UsedDesignerBefore;
        DesignerExperience = d.DesignerExperience;
        DesignerExperienceReason = d.DesignerExperienceReason;
        HiringReasons = d.HiringReasons;
        PreferredStyle = d.PreferredStyle;
        PreferredColors = d.PreferredColors;
        FurnitureStatus = d.FurnitureStatus;
        ExpectedBudget = d.ExpectedBudget;
        TimelinePressure = d.TimelinePressure;
        ArchitecturalRequests = d.ArchitecturalRequests;
        OtherNeeds = d.OtherNeeds;
        IsRented = d.IsRented;
        FinishingPurpose = d.FinishingPurpose;
    }
}
