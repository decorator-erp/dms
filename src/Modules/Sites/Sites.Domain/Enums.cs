namespace Sites.Domain;

// Domain-internal copies of the enums Sites.Contracts exposes publicly, mirroring Crm.Domain.Enums
// alongside Crm.Contracts.Enums. Domain never references Contracts (reference matrix: Domain may
// reference SharedKernel only), so the duplication is structural, not a choice. Sites.Application owns
// the mapping between the two pairs, exactly as Crm.Application's CrmEnumMapping does.
//
// Values must stay identical to the Contracts copies in both directions -- these are persisted as ints.

internal enum SiteStatus
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Rejected = 4,
}

internal enum SiteDocumentKind
{
    Sketch = 1,
    Video = 2,
    Photo = 3,
}

/// <summary>Numbered deliberately unlike CRM's <c>CatalogAttributeType</c>; the two are never
/// cross-compared and nothing casts between them.</summary>
internal enum SiteItemDataType
{
    Number = 1,
    Text = 2,
    Bool = 3,
    Select = 4,
    Date = 5,
}

// The five unit descriptors a survey shares with a CRM design brief -- the stated-vs-measured split.
// See Sites.Contracts.Enums for why these are duplicated rather than referenced from Crm.Contracts.

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
