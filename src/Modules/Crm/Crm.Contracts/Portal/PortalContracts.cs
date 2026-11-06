using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
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
/// <summary>B-1 (H2/X-7): one of the client's own meetings with its slots — what the client needs to
/// answer a staff proposal. No employee ids: <see cref="PortalMeetingSlotResponse.ProposedByMe"/> says only
/// whether the client's side proposed the slot.</summary>
public sealed record PortalMeetingDetailResponse(
    Guid Id, Guid ProjectId, string Title, MeetingType Type, MeetingStatus Status, DateTime? ScheduledAtUtc,
    int? DurationMinutes, string? Location, DateTime? CanceledAtUtc, string? CancelReason,
    IReadOnlyList<PortalMeetingSlotResponse> Slots);

public sealed record PortalMeetingSlotResponse(
    Guid Id, DateTime StartsAtUtc, DateTime EndsAtUtc, MeetingSlotStatus Status, bool ProposedByMe);

/// <summary>B-1: the calling client's own meeting by id — 404 for any other client's or an unknown one.
/// Portal-only; the client never holds <c>crm.meetings.read</c> (MT-N1).</summary>
public sealed record GetMyPortalMeetingByIdQuery(string AppUserId, Guid MeetingId)
    : IQuery<PortalMeetingDetailResponse>, ICrmScoped;

public sealed record PortalBriefViewResponse(BriefHeadBlock? Head, IReadOnlyList<PortalBriefSpaceResponse> Spaces);

/// <summary>A-14 (B-L6): a brief-space entry as the client sees it — the office DTO
/// (<see cref="CrmBriefSpaceResponse"/>) minus who wrote it and the office-side task and meeting links.</summary>
public sealed record PortalBriefSpaceResponse(
    Guid Id, Guid BriefId, Guid? SpaceId, int? CountOrPieces, string? Requirements,
    CrmApprovalState ApprovalStatus, Guid? SupersedesSpaceId, bool IsCurrent);

/// <summary>Meetings visible to the calling client's own projects only — resolved via
/// <c>ICrmAccessGuard</c>, never a project id supplied by the caller. B-15 (MT-N7): paged, newest first —
/// <see cref="PageNumber"/> from 1, <see cref="PageSize"/> 1..200.</summary>
public sealed record GetMyPortalMeetingsQuery(string AppUserId, int PageNumber = 1, int PageSize = 50)
    : IQuery<PagedResult<PortalMeetingResponse>>, ICrmScoped;

/// <summary>The client's own brief for one project. With <see cref="StageId"/>, that stage's brief;
/// without it, the most recently created brief on the project (a project has one brief per
/// stage).</summary>
public sealed record GetMyPortalBriefQuery(string AppUserId, Guid ProjectId, Guid? StageId = null)
    : IQuery<PortalBriefViewResponse>, ICrmScoped;

/// <summary>The client audience's round diff (no <c>ExpectedBudget</c>, no <c>LastMeetingId</c>) — only
/// for a brief on one of the caller's own projects; otherwise 404.</summary>
public sealed record GetMyPortalBriefRoundDiffQuery(string AppUserId, Guid BriefId, int Round)
    : IQuery<BriefHeadDiffResponse>, ICrmScoped;

/// <summary>B1.2: the decision a client sends from the portal. Same ordinals as
/// <c>Approvals.Contracts.ApprovalDecision</c> (a Contracts project may not reference another module's, Rule
/// 15, so CRM names its own). The portal accepts only <see cref="Approved"/> and
/// <see cref="ChangesRequested"/>; <see cref="Rejected"/> is listed so a decided row can report it, and is a
/// 400 (<c>crm.portal.InvalidDecision</c>) when sent.</summary>
public enum PortalApprovalDecision
{
    Approved = 1,
    Rejected = 2,
    ChangesRequested = 3,
}

/// <summary>B1.3: what a portal approval is about. Same ordinals as
/// <c>Approvals.Contracts.ApprovalSubjectType</c>, CRM's three subjects only.</summary>
public enum PortalApprovalSubjectType
{
    DesignBrief = 2,
    RequirementItem = 3,
    BriefSpace = 4,
}

/// <summary>B1.2: the client's own decision on a brief-round, brief-space-entry or requirement-item
/// approval, gated by <c>crm.approvals.decide_own</c>. The handler proves the approval's subject is on one
/// of the caller's own projects (else 404) and then decides through <c>Approvals.Contracts.IApprovalDecider</c>.
/// <see cref="Comment"/> is required for <see cref="PortalApprovalDecision.ChangesRequested"/>, at most 2000
/// characters.</summary>
public sealed record DecideMyPortalApprovalCommand(
    string AppUserId, Guid ApprovalId, PortalApprovalDecision Decision, string? Comment)
    : ICommand, ICrmScoped;

/// <summary>The client's list of their own brief's rounds — voided rounds left out (the client's decision
/// on them never counted) — only for a brief on one of the caller's own projects; otherwise 404.</summary>
public sealed record GetMyPortalBriefRoundsQuery(string AppUserId, Guid BriefId)
    : IQuery<IReadOnlyList<CrmBriefRoundResponse>>, ICrmScoped;

/// <summary>B1.3: one row of the client's approvals inbox. Portal-only: no employee ids, no office fields.
/// <see cref="ProjectName"/> and <see cref="StageName"/> come from Projects; <see cref="Round"/> is the brief
/// round or the entry/item send it was sent under (null when it cannot be told, e.g. a legacy id);
/// <see cref="Title"/> is the item's title or the entry's space name (null for a brief round and for a
/// general entry); <see cref="BriefId"/> is the brief for a brief round or entry. <see cref="Comment"/> is the
/// client's own comment, null when the decision was not the caller's.</summary>
public sealed record PortalApprovalResponse(
    Guid Id, PortalApprovalSubjectType SubjectType, Guid SubjectId, Guid ProjectId, string ProjectName,
    Guid? BriefId, Guid? StageId, string? StageName, int? Round, string? Title, DateTime RequestedAtUtc,
    DateTime? DueAtUtc, PortalApprovalDecision? Decision, DateTime? DecidedAtUtc, string? Comment);

/// <summary>B1.3: the calling client's own approvals. <see cref="Status"/> is <c>pending</c> (default) or
/// <c>decided</c> (else <c>crm.portal.InvalidStatus</c>, 400); <see cref="PageNumber"/> 1..1,000,000,
/// <see cref="PageSize"/> 1..200. Paged on the Approvals side; a row whose project is not the caller's is
/// dropped after paging, so <c>TotalCount</c> can over-count by those rows.</summary>
public sealed record GetMyPortalApprovalsQuery(
    string AppUserId, string? Status = null, int PageNumber = 1, int PageSize = 50)
    : IQuery<PagedResult<PortalApprovalResponse>>, ICrmScoped;

/// <summary>crm-complete F1 (D5): one row of the client's requirement-items list. Portal-safe: no office
/// authors, notes, tasks or meetings. <see cref="Decision"/>/<see cref="DecisionComment"/>/<see cref="DecidedAtUtc"/>
/// are the item's latest approval's decision, null while it is undecided or was never sent.</summary>
public sealed record PortalRequirementItemResponse(
    Guid Id, Guid ProjectId, string ProjectName, Guid PeriodId, RequirementCategory Category, string Title,
    string? Description, string? Room, RequirementPriority Priority, Guid? SpaceTypeId, CrmApprovalState ApprovalStatus,
    bool SubmittedByMe, PortalApprovalDecision? Decision, string? DecisionComment, DateTime? DecidedAtUtc, DateTime CreatedAtUtc);

/// <summary>F1: the calling client's items across its active projects, newest first. Office drafts are hidden;
/// a project or period that isn't the client's is 404.</summary>
public sealed record GetMyPortalRequirementItemsQuery(
    string AppUserId, Guid? ProjectId = null, Guid? PeriodId = null, CrmApprovalState? Status = null,
    int PageNumber = 1, int PageSize = 50)
    : IQuery<PagedResult<PortalRequirementItemResponse>>, ICrmScoped;

/// <summary>crm-complete F2 (D5): the client's own profile. Field names follow the frontend's
/// <c>ClientProfile</c>; nothing office-only (no notes, owner, job title or marital status).</summary>
public sealed record PortalProfileResponse(
    PortalClientIdentityResponse Client, IReadOnlyList<PortalProfileContactResponse> Contacts,
    IReadOnlyList<PortalProfileProjectResponse> Projects);

/// <summary><see cref="FirstName"/>/<see cref="LastName"/>/<see cref="PhoneNumber"/> come from the primary
/// contact (falling back to the login's display name); <see cref="Email"/> is the portal login's.</summary>
public sealed record PortalClientIdentityResponse(
    Guid Id, string Name, string FirstName, string LastName, string? Email, string? PhoneNumber, string Address);

public sealed record PortalProfileContactResponse(
    Guid Id, string FirstName, string LastName, string? Email, string? Phone, string? Mobile, ContactRole Role, bool IsPrimary);

public sealed record PortalProfileProjectResponse(
    Guid Id, string Code, string Name, string Status, decimal ProgressPercentage, Guid? ActiveStageId, string? ActiveStageName,
    Guid? OpenPeriodId);

public sealed record GetMyPortalProfileQuery(string AppUserId) : IQuery<PortalProfileResponse>, ICrmScoped;
