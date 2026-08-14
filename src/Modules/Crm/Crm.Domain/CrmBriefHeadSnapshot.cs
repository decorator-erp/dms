using SharedKernel;

namespace Crm.Domain;

/// <summary>
/// An immutable copy of the brief HEAD captured at each SEND (round creation) — ported from
/// <c>../backend/Domain/Entities/CrmBriefHeadSnapshot.cs</c>. The head is edit-in-place so pre-edit
/// values are destroyed on write; this snapshot preserves the approved-state facts per round. One row
/// per (BriefId, Round); never updated after creation.
///
/// <para><see cref="DiffFrom"/> is new in this port — the backend's own doc comment on
/// <c>CrmBriefDomainService</c> names the snapshot as "FR-029 per-round diff source" but the backend
/// never implements the diff itself. This is genuinely new work per <c>PLAN-PHASE4.md</c>'s task text
/// ("port faithfully and unit-test the diff specifically") rather than a port, and stays a pure
/// field-by-field comparison with no I/O — no reason for it to live outside Domain.</para>
/// </summary>
internal sealed class CrmBriefHeadSnapshot : Entity
{
    public Guid BriefId { get; private set; }
    public Guid ProjectId { get; private set; }
    public int Round { get; private set; }

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
    public decimal? ExpectedBudget { get; private set; }
    public TimelinePressure? TimelinePressure { get; private set; }
    public string? ArchitecturalRequests { get; private set; }
    public string? OtherNeeds { get; private set; }
    public ProjectSector? Sector { get; private set; }
    public bool? IsRented { get; private set; }
    public FinishingPurpose? FinishingPurpose { get; private set; }
    public Guid? LastMeetingId { get; private set; }

    private CrmBriefHeadSnapshot() { }

    /// <summary>Captures <paramref name="brief"/>'s current head state as the snapshot for
    /// <paramref name="round"/>.</summary>
    public static CrmBriefHeadSnapshot Create(CrmDesignBrief brief, int round) =>
        new()
        {
            BriefId = brief.Id,
            ProjectId = brief.ProjectId,
            Round = round,
            UnitType = brief.UnitType,
            UnitCondition = brief.UnitCondition,
            Address = brief.Address,
            FloorNo = brief.FloorNo,
            UnitsPerFloor = brief.UnitsPerFloor,
            AreaSqm = brief.AreaSqm,
            BuildingKind = brief.BuildingKind,
            IsLicensed = brief.IsLicensed,
            Position = brief.Position,
            ElevatorStatus = brief.ElevatorStatus,
            ReceivesFrequentVisitors = brief.ReceivesFrequentVisitors,
            VisitorsNote = brief.VisitorsNote,
            UsedDesignerBefore = brief.UsedDesignerBefore,
            DesignerExperience = brief.DesignerExperience,
            DesignerExperienceReason = brief.DesignerExperienceReason,
            HiringReasons = brief.HiringReasons,
            PreferredStyle = brief.PreferredStyle,
            PreferredColors = brief.PreferredColors,
            FurnitureStatus = brief.FurnitureStatus,
            ExpectedBudget = brief.ExpectedBudget,
            TimelinePressure = brief.TimelinePressure,
            ArchitecturalRequests = brief.ArchitecturalRequests,
            OtherNeeds = brief.OtherNeeds,
            Sector = brief.Sector,
            IsRented = brief.IsRented,
            FinishingPurpose = brief.FinishingPurpose,
            LastMeetingId = brief.LastMeetingId,
        };

    /// <summary>The names of every editable head field whose value differs between
    /// <paramref name="previous"/> and this snapshot — the per-round diff FR-029 asks for. Compares
    /// only the editable head fields (not <see cref="Id"/>/<see cref="BriefId"/>/<see cref="Round"/>),
    /// since those aren't part of "what changed" from the client's perspective.</summary>
    public IReadOnlyList<string> DiffFrom(CrmBriefHeadSnapshot previous)
    {
        var changed = new List<string>();

        void Compare<T>(string name, T mine, T theirs)
        {
            if (!EqualityComparer<T>.Default.Equals(mine, theirs))
                changed.Add(name);
        }

        Compare(nameof(UnitType), UnitType, previous.UnitType);
        Compare(nameof(UnitCondition), UnitCondition, previous.UnitCondition);
        Compare(nameof(Address), Address, previous.Address);
        Compare(nameof(FloorNo), FloorNo, previous.FloorNo);
        Compare(nameof(UnitsPerFloor), UnitsPerFloor, previous.UnitsPerFloor);
        Compare(nameof(AreaSqm), AreaSqm, previous.AreaSqm);
        Compare(nameof(BuildingKind), BuildingKind, previous.BuildingKind);
        Compare(nameof(IsLicensed), IsLicensed, previous.IsLicensed);
        Compare(nameof(Position), Position, previous.Position);
        Compare(nameof(ElevatorStatus), ElevatorStatus, previous.ElevatorStatus);
        Compare(nameof(ReceivesFrequentVisitors), ReceivesFrequentVisitors, previous.ReceivesFrequentVisitors);
        Compare(nameof(VisitorsNote), VisitorsNote, previous.VisitorsNote);
        Compare(nameof(UsedDesignerBefore), UsedDesignerBefore, previous.UsedDesignerBefore);
        Compare(nameof(DesignerExperience), DesignerExperience, previous.DesignerExperience);
        Compare(nameof(DesignerExperienceReason), DesignerExperienceReason, previous.DesignerExperienceReason);
        Compare(nameof(HiringReasons), HiringReasons, previous.HiringReasons);
        Compare(nameof(PreferredStyle), PreferredStyle, previous.PreferredStyle);
        Compare(nameof(PreferredColors), PreferredColors, previous.PreferredColors);
        Compare(nameof(FurnitureStatus), FurnitureStatus, previous.FurnitureStatus);
        Compare(nameof(ExpectedBudget), ExpectedBudget, previous.ExpectedBudget);
        Compare(nameof(TimelinePressure), TimelinePressure, previous.TimelinePressure);
        Compare(nameof(ArchitecturalRequests), ArchitecturalRequests, previous.ArchitecturalRequests);
        Compare(nameof(OtherNeeds), OtherNeeds, previous.OtherNeeds);
        Compare(nameof(Sector), Sector, previous.Sector);
        Compare(nameof(IsRented), IsRented, previous.IsRented);
        Compare(nameof(FinishingPurpose), FinishingPurpose, previous.FinishingPurpose);
        Compare(nameof(LastMeetingId), LastMeetingId, previous.LastMeetingId);

        return changed;
    }
}
