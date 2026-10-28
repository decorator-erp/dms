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
    FinishingPurpose? FinishingPurpose = null,
    ProjectSector? Sector = null);

/// <summary>
/// Ported from <c>../backend/Domain/Entities/CrmDesignBrief.cs</c> — the digitized "Application Form"
/// head, a true 1:1 with a project stage, edited in place. Editing a decided head returns it to
/// <see cref="ApprovalState.Draft"/>; it is re-confirmed only by an explicit send. The approval attempts
/// form the trail. The append-only ledger lives one level down in <see cref="CrmBriefSpace"/> — the head
/// is exempt from the ledger rule.
///
/// <para><b>Every round is bound to its approval.</b> <see cref="BeginRound"/> records the approval id
/// the round was sent under in <see cref="PendingApprovalId"/>, the head is locked while it is pending,
/// and <see cref="MarkApproved"/>/<see cref="MarkRejected"/> only accept the decision of THAT approval.
/// Before this, a decision approved whatever the head held when it arrived — including edits made after
/// the client saw it — and an edit to an Approved head flipped it to PendingApproval with no approval
/// open at all.</para>
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
    /// <summary>Required: a brief is one stage's brief, unique per (project, stage). Nullable before M1,
    /// when a stage-less brief could never satisfy a gate and Postgres let any number of them share a
    /// project.</summary>
    public Guid ProjectStageId { get; private set; }
    public Guid CreatedById { get; private set; }
    public Guid? LastMeetingId { get; private set; }
    public ApprovalState Status { get; private set; }

    /// <summary>The approval the current round was sent under; set only while
    /// <see cref="ApprovalState.PendingApproval"/>.</summary>
    public Guid? PendingApprovalId { get; private set; }

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

    /// <summary>The sector is per brief (D-4). Set from the head form when sent; on create, otherwise filled
    /// from the intake (first brief) or copied from the project's latest brief at that moment (later briefs).
    /// A null in the form keeps the stored value, so once set it can be changed but never cleared back to
    /// null. Editing an older brief's sector does not reach briefs already created from it.</summary>
    public ProjectSector? Sector { get; private set; }

    public bool? IsRented { get; private set; }
    public FinishingPurpose? FinishingPurpose { get; private set; }

    private CrmDesignBrief() { }

    public static CrmDesignBrief Create(
        Guid projectId, Guid createdById, Guid projectStageId, CrmBriefHeadData data)
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

    /// <summary>Edit-in-place — the head is exempt from the ledger rule, but locked while a round is
    /// pending (the client is deciding on exactly what was sent). Editing a decided head (Approved or
    /// Rejected) returns it to <see cref="ApprovalState.Draft"/>: it is re-confirmed only by a new
    /// <see cref="BeginRound"/>.</summary>
    public Result UpdateHead(CrmBriefHeadData data, Guid? lastMeetingId)
    {
        if (Status == ApprovalState.PendingApproval)
        {
            return CrmBriefErrors.HeadLockedPendingApproval;
        }

        Apply(data);
        if (lastMeetingId is not null)
        {
            LastMeetingId = lastMeetingId;
        }

        Status = ApprovalState.Draft;
        return Result.Success();
    }

    /// <summary>Opens a round under <paramref name="approvalId"/>. Only a Draft or Rejected head can be
    /// sent; a pending one already has its round, an approved one must be edited (→ Draft) first.</summary>
    internal Result BeginRound(Guid approvalId)
    {
        if (Status is not (ApprovalState.Draft or ApprovalState.Rejected))
        {
            return CrmBriefErrors.NotSendable;
        }

        Status = ApprovalState.PendingApproval;
        PendingApprovalId = approvalId;
        return Result.Success();
    }

    /// <summary>Moves the pending round onto <paramref name="nextApprovalId"/>, voiding the approval it was
    /// sent under. Used when that approval comes back already Approved although the client never saw the
    /// round's content (its snapshot rolled back with a failed CRM commit): the approval cannot count, so
    /// the head is re-sent as a new round. Only the round's own approval can be voided.</summary>
    internal Result VoidRound(Guid voidedApprovalId, Guid nextApprovalId)
    {
        if (Status != ApprovalState.PendingApproval || PendingApprovalId != voidedApprovalId)
        {
            return CrmBriefErrors.NotPending;
        }

        PendingApprovalId = nextApprovalId;
        return Result.Success();
    }

    /// <summary>D-10 (A-F1): binds a legacy pending round that lost its approval id to
    /// <paramref name="approvalId"/>.</summary>
    internal Result AdoptPendingApproval(Guid approvalId)
    {
        if (Status != ApprovalState.PendingApproval || PendingApprovalId is not null)
        {
            return CrmBriefErrors.NotPending;
        }

        PendingApprovalId = approvalId;
        return Result.Success();
    }

    /// <summary>Applies <paramref name="approvalId"/>'s approval — only if it is the approval the current
    /// round was sent under.</summary>
    internal Result MarkApproved(Guid approvalId)
    {
        if (Status != ApprovalState.PendingApproval || PendingApprovalId != approvalId)
        {
            return CrmBriefErrors.NotPending;
        }

        Status = ApprovalState.Approved;
        PendingApprovalId = null;
        return Result.Success();
    }

    internal Result MarkRejected(Guid approvalId)
    {
        if (Status != ApprovalState.PendingApproval || PendingApprovalId != approvalId)
        {
            return CrmBriefErrors.NotPending;
        }

        Status = ApprovalState.Rejected;
        PendingApprovalId = null;
        return Result.Success();
    }

    /// <summary>Hop 2 of lead conversion — copy-once from the converted lead's intake into the project's
    /// first brief, a freshly created Draft head (<c>CreateCrmDesignBriefCommandHandler</c>). Guard: only while <see cref="Status"/> is Draft; never re-applied.</summary>
    internal Result SeedFromIntake(
        ProjectSector? sector, string? unitAddress, decimal? areaSqm, bool? usedDesignerBefore,
        PastExperience? designerExperience, string? designerExperienceReason,
        UnitType? residentialUnitType, UnitCondition? residentialUnitCondition,
        bool? residentialIsLicensed, bool? residentialIsRented, FinishingPurpose? residentialFinishingPurpose)
    {
        if (Status != ApprovalState.Draft)
        {
            return CrmBriefErrors.NotDraft;
        }

        Sector ??= sector;
        Address ??= unitAddress;
        AreaSqm ??= areaSqm;
        UsedDesignerBefore ??= usedDesignerBefore;
        DesignerExperience ??= designerExperience;
        DesignerExperienceReason ??= designerExperienceReason;

        if (residentialUnitType is not null)
        {
            UnitType = residentialUnitType.Value;
        }

        if (residentialUnitCondition is not null)
        {
            UnitCondition = residentialUnitCondition.Value;
        }

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
        if (d.Sector is not null)
        {
            Sector = d.Sector;
        }
    }
}
