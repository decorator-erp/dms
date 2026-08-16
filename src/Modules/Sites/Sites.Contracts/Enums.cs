namespace Sites.Contracts;

/// <summary>
/// A survey's lifecycle, ported from <c>../backend/Domain/Enums/SiteStatus.cs</c>. Sites runs its own
/// submit/approve cycle rather than routing through the Approvals module: the who/when/why live on the
/// <c>Site</c> row itself (<c>SubmittedAtUtc</c>/<c>DecidedAtUtc</c>/<c>DecidedById</c>/
/// <c>DecisionNote</c>), and <c>Approvals.Contracts.ApprovalSubjectType</c> has no <c>Site</c> value —
/// verified, it has exactly five (GateCondition, DesignBrief, RequirementItem, BriefSpace,
/// ProjectDocument). Wiring Sites into Approvals would be a design change, not a port.
/// </summary>
public enum SiteStatus
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Rejected = 4,
}

/// <summary>What a linked document is, ported verbatim (اسكتش / فيديو / صور).</summary>
public enum SiteDocumentKind
{
    Sketch = 1,
    Video = 2,
    Photo = 3,
}

/// <summary>
/// The shape of one captured field. Deliberately numbered differently from CRM's
/// <c>CrmCatalogAttributeType</c> (which is Text=1, Number=2) — the two are never cross-compared and
/// nothing casts between them; the backend's own enum carries the same warning.
/// </summary>
public enum SiteItemDataType
{
    Number = 1,
    Text = 2,
    Bool = 3,
    Select = 4,
    Date = 5,
}

// ── Unit descriptors ───────────────────────────────────────────────────────────
//
// These five are the "what kind of unit is this" vocabulary a survey shares with a CRM design brief —
// the stated-vs-measured split: the brief records what the client said, the site records what the
// surveyor measured, and comparing them is the point.
//
// They are DUPLICATED here rather than referenced from Crm.Contracts, deliberately. A shared enum is
// not a cross-module read: taking a Sites.Contracts -> Crm.Contracts dependency for five closed value
// lists would make CRM's public surface part of Sites' own domain vocabulary, so a CRM renumbering
// would silently change what a stored site column means. Sites.Domain carries its own internal copies
// again for the same reason, exactly as Crm.Domain does alongside Crm.Contracts. Values are identical
// to the backend's single Domain/Enums definitions and must stay that way — both modules persist them
// as ints.

public enum UnitType
{
    Apartment = 1,
    Villa = 2,
    Duplex = 3,
    Studio = 4,
}

public enum UnitCondition
{
    BareBrick = 1,
    SemiFinished = 2,
    Renovation = 3,
}

public enum UnitPosition
{
    Corner = 1,
    Facade = 2,
}

public enum BuildingKind
{
    Tower = 1,
    Private = 2,
}

public enum ElevatorStatus
{
    Working = 1,
    NotWorking = 2,
    None = 3,
}
