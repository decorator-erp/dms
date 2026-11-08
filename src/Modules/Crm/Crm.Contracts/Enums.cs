namespace Crm.Contracts;

// Every enum here is mirrored by an internal twin in Crm.Domain — forced duplication, same reasoning
// as Approvals' G4-01/G4-02 and Projects' G5-01/G5-02: Domain may reference SharedKernel and nothing
// else, so it can never see Contracts. Crm.Application converts between the two pairs via
// CrmEnumMapping, whose parity with these declarations is asserted value-by-value in
// Crm.Application.Tests.EnumMappingParityTests.

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/LeadSource.cs</c>.</summary>
public enum LeadSource
{
    Website = 1,
    Referral = 2,
    SocialMedia = 3,
    Phone = 4,
    Event = 5,
    Other = 6,
}

/// <summary>Ported verbatim. <see cref="Converted"/> is reachable only through the conversion
/// command, never through a plain status change — see <c>CrmLead.ChangeStatus</c>.</summary>
public enum LeadStatus
{
    New = 1,
    Contacted = 2,
    Qualified = 3,
    Unqualified = 4,
    Converted = 5,
}

/// <summary>Relationship of a CRM contact row to the client. Ported verbatim.</summary>
public enum ContactRole
{
    Owner = 1,
    Spouse = 2,
    FamilyMember = 3,
    Representative = 4,
    Other = 5,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/MeetingStatus.cs</c>.</summary>
public enum MeetingStatus
{
    Requested = 1,
    Scheduled = 2,
    Rescheduled = 3,
    Completed = 4,
    Canceled = 5,
    NoShow = 6,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/MeetingSlotStatus.cs</c>.</summary>
public enum MeetingSlotStatus
{
    Proposed = 1,
    Accepted = 2,
    Declined = 3,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/MeetingType.cs</c>.</summary>
public enum MeetingType
{
    InOffice = 1,
    OnSite = 2,
    Online = 3,
}

/// <summary>
/// Confirmation state of a CRM artifact (brief head, gathering entry, requirement item), ported from
/// <c>../backend/Domain/Enums/ApprovalState.cs</c>. <see cref="NotRequired"/> = client-authored
/// content, trusted immediately and locked exactly like <see cref="Approved"/> — the ledger rule
/// applies to both.
///
/// <para>Deliberately named <c>CrmApprovalState</c> rather than the backend's <c>ApprovalState</c>:
/// <c>Approvals.Contracts.ApprovalState</c> already exists and means something different (the
/// <c>Approval</c> aggregate's own computed lifecycle, see its remarks). <c>Crm.Infrastructure</c>
/// references both assemblies, so identical names would force every file that touches an approval to
/// alias one of them. The values match position-for-position, but the two are not interchangeable and
/// no implicit conversion exists between them.</para>
/// </summary>
public enum CrmApprovalState
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Rejected = 4,
    NotRequired = 5,
}

/// <summary>
/// Priority of a requirement item, ported from <c>../backend/Domain/Enums/TaskPriority.cs</c>. Named
/// <c>RequirementPriority</c> here for the same reason <see cref="CrmApprovalState"/> is renamed:
/// <c>Projects.Contracts.TaskPriority</c> already exists and belongs to a project task, not a CRM
/// requirement. The two happen to share values today; nothing keeps them in step and nothing should.
/// </summary>
public enum RequirementPriority
{
    Low = 1,
    Medium = 2,
    High = 3,
    Urgent = 4,
}

/// <summary>
/// نوع المشروع — the business sector a project belongs to; drives which intake sub-form the UI shows
/// and which sector child row the intake may carry. Ported verbatim.
///
/// <para>CRM owns this, not Projects: <c>Projects.Domain.Project</c>'s own remarks record that
/// <c>ProjectSector</c> was deliberately left out of Projects at G5-01 as "a CRM lead-intake
/// concept". This is that concept arriving in its own module.</para>
/// </summary>
public enum ProjectSector
{
    Residential = 1,
    Medical = 2,
    Administrative = 3,
    FoodAndBeverage = 4,
    Retail = 5,
    MegaProject = 6,
}

/// <summary>Why the client hired a designer — combinable, stored as an int bitmask.</summary>
[Flags]
public enum HiringReason
{
    None = 0,
    ReliefFromCraftsmen = 1,
    SaveMoneyAndTime = 2,
    OrganizeAndComfort = 4,
}

/// <summary>How a previous designer engagement went. Phase 2 correctly refused this for HR — a
/// past-designer opinion is a CRM intake fact, not an employment record.</summary>
public enum PastExperience
{
    Good = 1,
    Bad = 2,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/TimelinePressure.cs</c>.</summary>
public enum TimelinePressure
{
    Tight = 1,
    Normal = 2,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/UnitType.cs</c>.</summary>
public enum UnitType
{
    Apartment = 1,
    Villa = 2,
    Duplex = 3,
    Studio = 4,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/UnitCondition.cs</c>.</summary>
public enum UnitCondition
{
    BareBrick = 1,
    SemiFinished = 2,
    Renovation = 3,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/UnitPosition.cs</c>.</summary>
public enum UnitPosition
{
    Corner = 1,
    Facade = 2,
}

/// <summary>برج / أهالي. Ported verbatim.</summary>
public enum BuildingKind
{
    Tower = 1,
    Private = 2,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/ElevatorStatus.cs</c>.</summary>
public enum ElevatorStatus
{
    Working = 1,
    NotWorking = 2,
    None = 3,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/FurnitureStatus.cs</c>.</summary>
public enum FurnitureStatus
{
    AlreadyOwned = 1,
    BuyPerDesign = 2,
    Mixed = 3,
}

/// <summary>الغرض من التشطيب. Ported verbatim.</summary>
public enum FinishingPurpose
{
    UnitFinishingOnly = 1,
    TurnkeyDelivery = 2,
    FinishingWithKitchen = 3,
    Restoration = 4,
    Renovation = 5,
}

/// <summary>
/// The value column a catalog attribute writes into. Exactly one value column is populated per value
/// row and it must be the one this type dictates — enforced in the entity factories via
/// <c>CatalogAttributeValueRules</c>, and half-enforced structurally by a table check constraint (a
/// Postgres CHECK cannot read the attribute row, so it can only prove "exactly one").
/// </summary>
public enum CatalogAttributeType
{
    Text = 1,
    Number = 2,
    Boolean = 3,
    Date = 4,

    /// <summary>Picklist — the value is a catalog attribute option id.</summary>
    Select = 5,
}

/// <summary>How many items may be recorded from a category's subtree in one space.</summary>
public enum CatalogSelectionMode
{
    None = 1,

    // A stored and serialized value the frontend already sends; renaming it would break that contract.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name",
        Justification = "Domain word (pick a single item), and a wire value clients already use.")]
    Single = 2,
    Multi = 3,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/RequirementCategory.cs</c>.</summary>
public enum RequirementCategory
{
    SpaceLayout = 1,
    Style = 2,
    ColorPalette = 3,
    Materials = 4,
    Furniture = 5,
    Lighting = 6,
    Budget = 7,
    Timeline = 8,
    Technical = 9,
    Other = 10,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/RequirementPeriodStatus.cs</c>.</summary>
public enum RequirementPeriodStatus
{
    Open = 1,
    Closed = 2,
}

/// <summary>
/// Where a requirement item came from. Null means a manual entry with no source reference; when set,
/// exactly the matching source id must be set too.
/// </summary>
public enum RequirementSource
{
    Meeting = 1,
    Brief = 2,
    Note = 3,
    ClientDirect = 4,
}

/// <summary>
/// What happens to an item recorded in a space. <see cref="ToBuy"/> is what feeds the buy list.
/// <see cref="NotApplicable"/> still counts as <i>recorded</i> — the space wasn't forgotten, the
/// decision was "no".
/// </summary>
public enum CrmProvisionStatus
{
    ToBuy = 1,
    ClientHas = 2,
    Reuse = 3,
    NotApplicable = 4,
}

/// <summary>Which kind of principal a CRM request is acting as — see <c>ICrmAccessGuard</c>.</summary>
public enum CrmActorKind
{
    Employee = 1,
    Client = 2,
}

/// <summary>How a sent brief round ended (GAP-2). <see cref="Pending"/>: the client hasn't decided yet.
/// <see cref="Voided"/>: the round was replaced without the client's decision counting.
/// <see cref="Unknown"/>: a round sent before outcomes were recorded, whose result can't be told from the
/// brief's current state.</summary>
public enum CrmBriefRoundOutcome
{
    Pending = 1,
    Approved = 2,
    ChangesRequested = 3,
    Voided = 4,
    Unknown = 5,
}

/// <summary>P2-A: what a client-portal share points at. Same values as <c>Crm.Domain.PortalShareKind</c>.
/// <see cref="Document"/>/<see cref="Image"/>: a project document (an image is <c>image/*</c>), by its document id;
/// <see cref="SitePhoto"/>: a site-survey attachment link of kind Photo, by its link id; <see cref="Stage"/>,
/// <see cref="Task"/>: a project stage or task.</summary>
public enum PortalShareKind
{
    Document = 1,
    Image = 2,
    SitePhoto = 3,
    Stage = 4,
    Task = 5,
}
