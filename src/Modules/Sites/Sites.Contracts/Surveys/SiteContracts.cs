using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;

namespace Sites.Contracts.Surveys;

// ── Requests ───────────────────────────────────────────────────────────────────

/// <summary>
/// The unit's measured facts, all optional — a survey starts as an empty shell and is filled in over
/// one or more visits. The stated counterpart is a CRM design brief; the two are allowed to disagree,
/// and that disagreement is what drives a variation.
/// </summary>
public sealed record SiteUnitDataRequest(
    string? Address = null,
    UnitType? UnitType = null,
    UnitCondition? UnitCondition = null,
    decimal? AreaSqm = null,
    int? FloorNo = null,
    int? UnitsPerFloor = null,
    BuildingKind? BuildingKind = null,
    UnitPosition? Position = null,
    ElevatorStatus? ElevatorStatus = null,
    bool? IsLicensed = null,
    bool? IsRented = null,
    string? UtilityMetersNote = null,
    DateOnly? SurveyedOn = null,
    string? Notes = null);

public sealed record CreateSiteRequest(Guid ProjectId, SiteUnitDataRequest Data);

public sealed record UpdateSiteRequest(SiteUnitDataRequest Data);

public sealed record DecideSiteRequest(bool Approved, string? Note = null);

// ── Responses ──────────────────────────────────────────────────────────────────

public sealed record SiteListItemResponse(
    Guid Id, string Code, SiteStatus Status, string? Address, DateOnly? SurveyedOn,
    DateTime CreatedAtUtc);

/// <summary>
/// Carries no <c>SourceBriefId</c>, unlike the backend's equivalent. The column exists on the entity
/// as provenance but nothing in this system writes it — <c>BackfillSitesFromBriefs</c> is a backend
/// data-migration utility that is not ported — so surfacing an always-null field on every detail read
/// would advertise a link that never exists. See the S1-01 entry and the deferred variance route.
///
/// <para><see cref="SubmittedById"/> (BF-2): the HR employee id of whoever submitted the current round — the
/// person the self-approval rule refuses alongside <see cref="CreatedById"/>. Null on a draft and after a rejected
/// survey is reopened for rework. Appended last, so the JSON change is additive.</para>
/// </summary>
public sealed record SiteDetailResponse(
    Guid Id, Guid ProjectId, string Code, SiteStatus Status,
    string? Address, UnitType? UnitType, UnitCondition? UnitCondition, decimal? AreaSqm,
    int? FloorNo, int? UnitsPerFloor, BuildingKind? BuildingKind, UnitPosition? Position,
    ElevatorStatus? ElevatorStatus, bool? IsLicensed, bool? IsRented, string? UtilityMetersNote,
    DateOnly? SurveyedOn, string? Notes,
    Guid CreatedById, DateTime? SubmittedAtUtc, DateTime? DecidedAtUtc, Guid? DecidedById,
    string? DecisionNote, bool IsActive, DateTime CreatedAtUtc,
    IReadOnlyList<SiteModuleRecordCountResponse> RecordCounts,
    Guid? SubmittedById);

public sealed record SiteModuleRecordCountResponse(Guid ModuleId, string Code, int RecordCount);

// ── Commands ───────────────────────────────────────────────────────────────────

/// <summary>
/// The one site command that is NOT <see cref="ISiteScoped"/> — there is no site yet to scope to, so
/// the handler authorizes itself against <c>IProjectAccessGuard</c> using <see cref="AppUserId"/>.
///
/// <para><see cref="CreatedById"/> is a pre-resolved HR employee id, not an app user id. The backend
/// resolves the employee inside the handler from the identity id; this port resolves it at the
/// ENDPOINT instead, following CRM's G9-14 precedent — "who is calling" questions belong at the
/// boundary where the HTTP principal already lives, and it keeps three otherwise-identical handlers
/// from each repeating the same lookup.</para>
/// </summary>
public sealed record CreateSiteCommand(CreateSiteRequest Request, Guid CreatedById, string AppUserId)
    : ICommand<Guid>;

public sealed record UpdateSiteCommand(Guid SiteId, UpdateSiteRequest Request, string AppUserId)
    : ICommand, ISiteScoped;

public sealed record DeleteSiteCommand(Guid SiteId, string AppUserId) : ICommand, ISiteScoped;

/// <summary>Draft → Submitted. The timestamp comes from an injected <c>TimeProvider</c>, never from
/// the caller.
///
/// <para><see cref="SubmittedById"/> is a pre-resolved HR employee id, resolved at the endpoint like
/// <c>DecidedById</c> (S10) — the event used to carry the survey's creator instead.</para>
/// </summary>
public sealed record SubmitSiteCommand(Guid SiteId, Guid SubmittedById, string AppUserId)
    : ICommand, ISiteScoped;

/// <summary>
/// Submitted → Approved | Rejected. <see cref="DecidedById"/> is a pre-resolved HR employee id — see
/// <see cref="CreateSiteCommand"/>'s remarks.
/// </summary>
public sealed record DecideSiteCommand(
    Guid SiteId, DecideSiteRequest Request, Guid DecidedById, string AppUserId)
    : ICommand, ISiteScoped;

/// <summary>Rejected → Draft, clearing all four decision stamps.</summary>
public sealed record ReopenSiteCommand(Guid SiteId, string AppUserId) : ICommand, ISiteScoped;

// ── Queries ────────────────────────────────────────────────────────────────────

/// <summary>
/// Not <see cref="ISiteScoped"/> — it lists sites rather than reading one, so there is no site id to
/// scope by. The handler checks project access itself, exactly as <see cref="CreateSiteCommand"/>
/// does.
/// </summary>
public sealed record GetSitesQuery(
    Guid ProjectId, SiteStatus? Status, string AppUserId, int PageIndex = 1, int PageSize = 25)
    : IQuery<PagedResult<SiteListItemResponse>>;

public sealed record GetSiteByIdQuery(Guid SiteId, string AppUserId)
    : IQuery<SiteDetailResponse>, ISiteScoped;
