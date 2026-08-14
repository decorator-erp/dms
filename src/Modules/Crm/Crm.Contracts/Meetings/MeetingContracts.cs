using BuildingBlocks.Messaging;

namespace Crm.Contracts.Meetings;

public sealed record CrmMeetingResponse(
    Guid Id, Guid ProjectId, Guid? PeriodId, string Title, MeetingType Type, MeetingStatus Status,
    DateTime? ScheduledAtUtc, int? DurationMinutes, string? Location,
    Guid? RequestedByEmployeeId, Guid? RequestedByContactId);

public sealed record CrmMeetingSlotResponse(
    Guid Id, Guid MeetingId, DateTime StartsAtUtc, DateTime EndsAtUtc, MeetingSlotStatus Status,
    Guid? ProposedByEmployeeId, Guid? ProposedByContactId);

/// <summary>Requester is employee XOR contact — the handler resolves exactly one from the acting
/// user via <c>ICrmAccessGuard</c>, never both, matching <c>CrmMeeting.Create</c>'s own guard.</summary>
public sealed record RequestCrmMeetingCommand(
    Guid ProjectId, string RequesterAppUserId, string Title, MeetingType Type,
    Guid? PeriodId = null, string? Location = null, int? DurationMinutes = null) : ICommand<Guid>;

public sealed record ProposeCrmMeetingSlotCommand(
    Guid MeetingId, string ProposerAppUserId, DateTime StartsAtUtc, DateTime EndsAtUtc) : ICommand<Guid>;

/// <summary>Accepting a slot schedules the meeting from it and publishes
/// <c>MeetingScheduledIntegrationEvent</c> in the same <c>SaveChangesAsync</c>.</summary>
public sealed record AcceptCrmMeetingSlotCommand(Guid SlotId) : ICommand;

public sealed record DeclineCrmMeetingSlotCommand(Guid SlotId) : ICommand;

public sealed record RescheduleCrmMeetingCommand(Guid MeetingId) : ICommand;

public sealed record CancelCrmMeetingCommand(Guid MeetingId, string Reason) : ICommand;

public sealed record CompleteCrmMeetingCommand(Guid MeetingId) : ICommand;

public sealed record MarkCrmMeetingNoShowCommand(Guid MeetingId) : ICommand;

public sealed record GetCrmMeetingByIdQuery(Guid MeetingId) : IQuery<CrmMeetingResponse>;

public sealed record GetCrmMeetingsByProjectQuery(Guid ProjectId) : IQuery<IReadOnlyList<CrmMeetingResponse>>;

public sealed record GetCrmMeetingSlotsByMeetingQuery(Guid MeetingId)
    : IQuery<IReadOnlyList<CrmMeetingSlotResponse>>;

/// <summary>The scheduler's free-slot proposal, ported from <c>ICrmSchedulerService</c>. Pure and
/// stateless — no persistence, no calendar lookup, just office-hours-driven candidate windows.</summary>
public sealed record FreeSlotResponse(DateTime StartsAtUtc, DateTime EndsAtUtc);

public sealed record GetFreeMeetingSlotsQuery(DateTime FromUtc, DateTime ToUtc)
    : IQuery<IReadOnlyList<FreeSlotResponse>>;
