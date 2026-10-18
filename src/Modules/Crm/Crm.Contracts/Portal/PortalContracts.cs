using BuildingBlocks.Messaging;

namespace Crm.Contracts.Portal;

/// <summary>
/// The client-facing read/write slice — the only consumer of <c>ICrmAccessGuard</c>'s client-actor
/// branch in this phase. Every type here resolves its acting client from
/// <see cref="ICrmScoped.AppUserId"/> inside the handler via <c>ICrmAccessGuard</c>, never from a
/// client id supplied by the request — a portal caller names no one but themselves.
/// </summary>
public sealed record PortalMeetingResponse(
    Guid Id, Guid ProjectId, string Title, MeetingStatus Status, DateTime? ScheduledAtUtc);

public sealed record PortalBriefResponse(Guid Id, Guid ProjectId, CrmApprovalState Status);

/// <summary>Meetings visible to the calling client's own projects only — resolved via
/// <c>ICrmAccessGuard</c>, never a project id supplied by the caller.</summary>
public sealed record GetMyPortalMeetingsQuery(string AppUserId)
    : IQuery<IReadOnlyList<PortalMeetingResponse>>, ICrmScoped;

/// <summary>The client's own brief for one project. With <see cref="StageId"/>, that stage's brief;
/// without it, the most recently created brief on the project (a project has one brief per
/// stage).</summary>
public sealed record GetMyPortalBriefQuery(string AppUserId, Guid ProjectId, Guid? StageId = null)
    : IQuery<PortalBriefResponse>, ICrmScoped;

/// <summary>The client's own decision on a brief/brief-space/requirement-item approval — routes
/// through <c>Approvals.Contracts</c>, gated by <c>crm.approvals.decide_own</c>.</summary>
public sealed record DecideMyPortalApprovalCommand(string AppUserId, Guid ApprovalId, bool Approve)
    : ICommand, ICrmScoped;
