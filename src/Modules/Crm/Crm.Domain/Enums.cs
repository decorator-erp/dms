namespace Crm.Domain;

// Mirrors Crm.Contracts' public enums — forced duplication, same reasoning as every other module's
// own Domain/Contracts split (Domain may reference SharedKernel only, so it can never see Contracts).
// Crm.Application converts between the two via CrmEnumMapping (added at G9-09), whose parity with
// these declarations is asserted value-by-value in Crm.Application.Tests.EnumMappingParityTests, same
// discipline as Projects.Application.ProjectsEnumMapping/EnumMappingParityTests.
//
// Added incrementally per Domain task, mirroring only the entities that task introduces — same
// pattern Projects.Domain's own Enums.cs followed across G5-02..G5-08.

/// <summary>Relationship of a CRM contact row to the client. Ported verbatim.</summary>
internal enum ContactRole
{
    Owner = 1,
    Spouse = 2,
    FamilyMember = 3,
    Representative = 4,
    Other = 5,
}

internal enum LeadSource
{
    Website = 1,
    Referral = 2,
    SocialMedia = 3,
    Phone = 4,
    Event = 5,
    Other = 6,
}

internal enum LeadStatus
{
    New = 1,
    Contacted = 2,
    Qualified = 3,
    Unqualified = 4,
    Converted = 5,
}

internal enum MeetingStatus
{
    Requested = 1,
    Scheduled = 2,
    Rescheduled = 3,
    Completed = 4,
    Canceled = 5,
    NoShow = 6,
}

internal enum MeetingSlotStatus
{
    Proposed = 1,
    Accepted = 2,
    Declined = 3,
}

internal enum MeetingType
{
    InOffice = 1,
    OnSite = 2,
    Online = 3,
}

[Flags]
internal enum HiringReason
{
    None = 0,
    ReliefFromCraftsmen = 1,
    SaveMoneyAndTime = 2,
    OrganizeAndComfort = 4,
}

internal enum PastExperience
{
    Good = 1,
    Bad = 2,
}

internal enum TimelinePressure
{
    Tight = 1,
    Normal = 2,
}

internal enum UnitType
{
    Apartment = 1,
    Villa = 2,
    Duplex = 3,
    Studio = 4,
}

internal enum UnitCondition
{
    BareBrick = 1,
    SemiFinished = 2,
    Renovation = 3,
}

internal enum UnitPosition
{
    Corner = 1,
    Facade = 2,
}

internal enum BuildingKind
{
    Tower = 1,
    Private = 2,
}

internal enum ElevatorStatus
{
    Working = 1,
    NotWorking = 2,
    None = 3,
}

internal enum FurnitureStatus
{
    AlreadyOwned = 1,
    BuyPerDesign = 2,
    Mixed = 3,
}

internal enum FinishingPurpose
{
    UnitFinishingOnly = 1,
    TurnkeyDelivery = 2,
    FinishingWithKitchen = 3,
    Restoration = 4,
    Renovation = 5,
}

/// <summary>See <c>Crm.Contracts.ProjectSector</c>'s remarks — CRM lead-intake data, deliberately not
/// a `Projects` concept per Phase 2's decision #6.</summary>
internal enum ProjectSector
{
    Residential = 1,
    Medical = 2,
    Administrative = 3,
    FoodAndBeverage = 4,
    Retail = 5,
    MegaProject = 6,
}
