using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;

namespace Crm.Contracts.Meetings;

/// <summary>
/// Reconciled at M1-01 against the backend's eleven <c>Application/Features/Crm/Meetings</c> feature
/// files. These records were declared at G9-01 with no handler to validate them and no caller to
/// constrain them, and several could not express what the flow actually needs — most starkly
/// <c>RescheduleCrmMeetingCommand</c>, which carried no new time. Changing them was free here and
/// would not have been once endpoints and callers existed.
///
/// <para><b>Actors are pre-resolved ids, never <c>AppUserId</c> strings.</b> The endpoint resolves the
/// caller through <c>CrmEndpointActors</c> (which already owns exactly this translation for
/// <c>WrittenById</c>/<c>OwnerId</c>/<c>ContactId</c> elsewhere in CRM) and passes a <c>Guid</c> in.
/// Only <c>ICrmScoped</c> Portal messages carry an <c>AppUserId</c>. The G9-01 shape put
/// <c>RequesterAppUserId</c>/<c>ProposerAppUserId</c> on two of these commands — the only non-Portal
/// CRM commands anywhere to do so — and it is dropped here.</para>
///
/// <para>Employee-vs-contact is modelled as two nullable ids rather than a discriminator, matching
/// <c>CrmMeeting.Create</c> and <c>CrmMeetingSlot.Create</c>, which enforce the same XOR themselves.
/// Each validator asserts it too, so a malformed command fails at 400 rather than reaching the
/// entity's own guard.</para>
/// </summary>
public sealed record CrmMeetingSlotResponse(
    Guid Id, Guid MeetingId, DateTime StartsAtUtc, DateTime EndsAtUtc, MeetingSlotStatus Status,
    Guid? ProposedByEmployeeId, Guid? ProposedByContactId);

/// <summary>Carries its slots, matching the backend's <c>MeetingResponse</c> — a meeting without its
/// candidate times is not usable by any caller that renders one, and the alternative is every consumer
/// making a second call. <see cref="GetCrmMeetingSlotsByMeetingQuery"/> still exists for the narrower
/// case: re-reading just the slot list after a propose/decline without refetching the meeting.
/// <c>ReminderSentAtUtc</c> is deliberately absent, as in the backend — it is a Notifications-side
/// bookkeeping stamp, not something a meeting's reader needs.</summary>
public sealed record CrmMeetingResponse(
    Guid Id, Guid ProjectId, Guid? PeriodId, string Title, MeetingType Type, MeetingStatus Status,
    DateTime? ScheduledAtUtc, int? DurationMinutes, string? Location,
    Guid? RequestedByEmployeeId, Guid? RequestedByContactId,
    DateTime? CanceledAtUtc, string? CancelReason, DateTime CreatedAtUtc,
    IReadOnlyList<CrmMeetingSlotResponse> Slots);

/// <summary>One proposed time. The backend's <c>SlotRequest</c>, renamed for the CRM prefix every other
/// type in this assembly carries.</summary>
public sealed record CrmMeetingSlotRequest(DateTime StartsAtUtc, DateTime EndsAtUtc);

/// <summary>
/// The office-side create: an employee proposes a meeting with one or more candidate slots, which is
/// the backend's <c>ProposeMeetingCommand</c>. G9-01 modelled creation as a single
/// <c>RequestCrmMeetingCommand</c> plus a separate <c>ProposeCrmMeetingSlotCommand</c>, which could not
/// express "a meeting and its slots in one transaction" — the only shape the backend actually has, and
/// the only one that leaves no window where a meeting exists with nothing to accept.
/// </summary>
public sealed record ProposeCrmMeetingCommand(
    Guid ProjectId, Guid RequestedByEmployeeId, string Title, MeetingType Type,
    IReadOnlyList<CrmMeetingSlotRequest> Slots, string UserId,
    Guid? PeriodId = null, string? Location = null, int? DurationMinutes = null) : ICommand<Guid>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Project(ProjectId);
}

/// <summary>
/// The client-side create, the backend's <c>RequestMeetingByClientCommand</c>: a contact requests a
/// meeting with exactly one proposed time. Distinct from <see cref="ProposeCrmMeetingCommand"/> because
/// the two differ in requester kind, slot cardinality, and route — the backend routes this one under
/// <c>/api/crm/portal</c>.
/// </summary>
public sealed record RequestCrmMeetingByClientCommand(
    Guid ProjectId, Guid RequestedByContactId, string Title, MeetingType Type,
    DateTime StartsAtUtc, DateTime EndsAtUtc,
    Guid? PeriodId = null, string? Location = null) : ICommand<Guid>;

/// <summary>Accepting a slot declines its siblings, schedules the meeting from it, and enqueues
/// <c>MeetingScheduledIntegrationEvent</c> in the same <c>SaveChangesAsync</c>. The actor is required:
/// only the counterparty of whoever proposed the slot may accept it, and the G9-01 record carried no
/// actor at all, so that rule was unenforceable.</summary>
/// <summary>B-3 (M1): the office accept — always project-scoped through the slot's meeting. The portal
/// sends <see cref="AcceptCrmMeetingSlotByContactCommand"/> instead.</summary>
public sealed record AcceptCrmMeetingSlotCommand(Guid SlotId, Guid ActingEmployeeId, string UserId) : ICommand, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.MeetingSlot(SlotId);
}

/// <summary>B-3 (M1): the client's accept. Not project-scoped (the client holds no project access); its
/// handler runs the tenant check against the contact's own client instead.</summary>
public sealed record AcceptCrmMeetingSlotByContactCommand(Guid SlotId, Guid ActingContactId) : ICommand;

/// <summary>Declining may offer counter-slots in the same call — the backend's
/// <c>DeclineSlotRequest.CounterSlots</c>, absent from the G9-01 record.</summary>
public sealed record DeclineCrmMeetingSlotCommand(
    Guid SlotId, Guid ActingEmployeeId, string UserId,
    IReadOnlyList<CrmMeetingSlotRequest>? CounterSlots = null) : ICommand, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.MeetingSlot(SlotId);
}

public sealed record DeclineCrmMeetingSlotByContactCommand(
    Guid SlotId, Guid ActingContactId, IReadOnlyList<CrmMeetingSlotRequest>? CounterSlots = null) : ICommand;

/// <summary>Moves the meeting back to <see cref="MeetingStatus.Rescheduled"/>, demotes the accepted
/// slot, and proposes a new time. The new time is the whole point of the operation and was missing
/// from the G9-01 record, which took only a meeting id.</summary>
public sealed record RescheduleCrmMeetingCommand(
    Guid MeetingId, Guid ActingEmployeeId, string UserId, DateTime StartsAtUtc, DateTime EndsAtUtc)
    : ICommand, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Meeting(MeetingId);
}

public sealed record RescheduleCrmMeetingByContactCommand(
    Guid MeetingId, Guid ActingContactId, DateTime StartsAtUtc, DateTime EndsAtUtc) : ICommand;

public sealed record CancelCrmMeetingCommand(Guid MeetingId, Guid ActingEmployeeId, string UserId, string Reason)
    : ICommand, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Meeting(MeetingId);
}

public sealed record CancelCrmMeetingByContactCommand(Guid MeetingId, Guid ActingContactId, string Reason) : ICommand;

/// <summary>Office-only, and takes no actor: the backend gates these two on
/// <c>Kind != Employee → AccessDenied</c> at the handler and has no portal route for either. Here the
/// permission strings (<c>crm.meetings.complete</c>/<c>crm.meetings.mark_no_show</c>, held by office
/// roles only) carry that, so the command stays a bare id.</summary>
public sealed record CompleteCrmMeetingCommand(Guid MeetingId, string UserId) : ICommand, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Meeting(MeetingId);
}

public sealed record MarkCrmMeetingNoShowCommand(Guid MeetingId, string UserId) : ICommand, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Meeting(MeetingId);
}

public sealed record GetCrmMeetingByIdQuery(Guid MeetingId, string UserId) : IQuery<CrmMeetingResponse>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Meeting(MeetingId);
}

/// <summary>
/// Paged, using this repo's own <see cref="PagedResult{T}"/> convention (<c>Hr.GetDepartmentsQuery</c>,
/// <c>Approvals.GetMyPendingApprovalsQuery</c>) rather than the backend's <c>Skip</c>/<c>Take</c> —
/// importing a second paging idiom for one query would be the more expensive choice. The backend's
/// bound of 200 per page is preserved in the validator.
///
/// <para><c>ProjectId</c> stays required. The backend's <c>SearchMeetingsQuery</c> makes it nullable,
/// where null means "every project I am scoped to", resolved via its
/// <c>ICrmAccessService.GetScopedProjectIdsAsync</c>. This port's <see cref="ICrmAccessGuard"/> has no
/// equivalent — it resolves who a caller is and exposes a single <c>HasReadAll</c> flag, with no
/// project-id scoping — so the cross-project search is a capability CRM cannot currently answer, not a
/// shape mismatch. It is recorded in the scope-boundaries table rather than silently dropped.</para>
/// </summary>
public sealed record GetCrmMeetingsByProjectQuery(Guid ProjectId, string UserId, int PageNumber, int PageSize)
    : IQuery<PagedResult<CrmMeetingResponse>>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Project(ProjectId);
}

public sealed record GetCrmMeetingSlotsByMeetingQuery(Guid MeetingId, string UserId)
    : IQuery<IReadOnlyList<CrmMeetingSlotResponse>>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Meeting(MeetingId);
}

/// <summary>The scheduler's free-slot proposal. Pure and stateless — no persistence, no calendar
/// lookup, just office-hours-driven candidate windows.</summary>
public sealed record FreeSlotResponse(DateTime StartsAtUtc, DateTime EndsAtUtc);

/// <summary>
/// <c>ProjectId</c> is the authorization scope and nothing else: the returned windows are office-wide
/// and identical for every project, because the scheduler reads office hours and no per-project or
/// per-employee calendar exists. That is the backend's behaviour too — it takes the project id purely
/// to run its scope check and never passes it to the scheduler. Stated here so a later reader does not
/// mistake it for a bug. It was missing entirely from the G9-01 record, which left the query
/// unauthorizable.
/// </summary>
public sealed record GetFreeMeetingSlotsQuery(Guid ProjectId, string UserId, DateTime FromUtc, DateTime ToUtc)
    : IQuery<IReadOnlyList<FreeSlotResponse>>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Project(ProjectId);
}
