using BuildingBlocks.Messaging;
using Crm.Contracts.Briefs;

namespace Crm.Contracts.Portal;

/// <summary>
/// The client-facing read/write slice — the only consumer of <c>ICrmAccessGuard</c>'s client-actor
/// branch in this phase. Every type here resolves its acting client from
/// <see cref="ICrmScoped.AppUserId"/> inside the handler via <c>ICrmAccessGuard</c>, never from a
/// client id supplied by the request — a portal caller names no one but themselves.
/// </summary>
public sealed record PortalMeetingResponse(
    Guid Id, Guid ProjectId, string Title, MeetingStatus Status, DateTime? ScheduledAtUtc);

/// <summary>The head as the client was last asked to approve it — the latest
/// <c>CrmBriefHeadSnapshot</c>, never the office's in-progress edits — plus the brief's live
/// <see cref="Status"/>. Deliberately has no <c>ExpectedBudget</c> (office money) and no
/// <c>LastMeetingId</c>.</summary>
public sealed record BriefHeadBlock(
    Guid BriefId, Guid ProjectId, Guid ProjectStageId, CrmApprovalState Status, int Round,
    UnitType UnitType, UnitCondition UnitCondition, string? Address, int? FloorNo, int? UnitsPerFloor,
    decimal? AreaSqm, BuildingKind? BuildingKind, bool? IsLicensed, UnitPosition? Position,
    ElevatorStatus? ElevatorStatus, bool? ReceivesFrequentVisitors, string? VisitorsNote,
    bool? UsedDesignerBefore, PastExperience? DesignerExperience, string? DesignerExperienceReason,
    HiringReason HiringReasons, string? PreferredStyle, string? PreferredColors,
    FurnitureStatus? FurnitureStatus, TimelinePressure? TimelinePressure, string? ArchitecturalRequests,
    string? OtherNeeds, ProjectSector? Sector, bool? IsRented, FinishingPurpose? FinishingPurpose);

/// <summary>The client's view of one brief. <see cref="Head"/> is null until the head has been sent at
/// least once; <see cref="Spaces"/> holds every entry except Drafts (the office's unsent work).</summary>
public sealed record PortalBriefViewResponse(BriefHeadBlock? Head, IReadOnlyList<PortalBriefSpaceResponse> Spaces);

/// <summary>A-14 (B-L6): a brief-space entry as the client sees it — the office DTO
/// (<see cref="CrmBriefSpaceResponse"/>) minus who wrote it and the office-side task and meeting links.</summary>
public sealed record PortalBriefSpaceResponse(
    Guid Id, Guid BriefId, Guid? SpaceId, int? CountOrPieces, string? Requirements,
    CrmApprovalState ApprovalStatus, Guid? SupersedesSpaceId, bool IsCurrent);

/// <summary>Meetings visible to the calling client's own projects only — resolved via
/// <c>ICrmAccessGuard</c>, never a project id supplied by the caller.</summary>
public sealed record GetMyPortalMeetingsQuery(string AppUserId)
    : IQuery<IReadOnlyList<PortalMeetingResponse>>, ICrmScoped;

/// <summary>The client's own brief for one project. With <see cref="StageId"/>, that stage's brief;
/// without it, the most recently created brief on the project (a project has one brief per
/// stage).</summary>
public sealed record GetMyPortalBriefQuery(string AppUserId, Guid ProjectId, Guid? StageId = null)
    : IQuery<PortalBriefViewResponse>, ICrmScoped;

/// <summary>The client audience's round diff (no <c>ExpectedBudget</c>, no <c>LastMeetingId</c>) — only
/// for a brief on one of the caller's own projects; otherwise 404.</summary>
public sealed record GetMyPortalBriefRoundDiffQuery(string AppUserId, Guid BriefId, int Round)
    : IQuery<BriefHeadDiffResponse>, ICrmScoped;

/// <summary>The client's own decision on a brief/brief-space/requirement-item approval — routes
/// through <c>Approvals.Contracts</c>, gated by <c>crm.approvals.decide_own</c>.</summary>
public sealed record DecideMyPortalApprovalCommand(string AppUserId, Guid ApprovalId, bool Approve)
    : ICommand, ICrmScoped;

/// <summary>The client's list of their own brief's rounds — voided rounds left out (the client's decision
/// on them never counted) — only for a brief on one of the caller's own projects; otherwise 404.</summary>
public sealed record GetMyPortalBriefRoundsQuery(string AppUserId, Guid BriefId)
    : IQuery<IReadOnlyList<CrmBriefRoundResponse>>, ICrmScoped;
