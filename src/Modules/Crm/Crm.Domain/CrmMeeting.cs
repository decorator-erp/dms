using SharedKernel;

namespace Crm.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/CrmMeeting.cs</c>. A client meeting with the scheduler
/// flow: requested (by employee or client — XOR), scheduled from an accepted slot, either side can
/// cancel/update until it starts.
/// </summary>
internal sealed class CrmMeeting : Entity, IAuditable
{
    public Guid ProjectId { get; private set; }
    public Guid? PeriodId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public MeetingType Type { get; private set; }
    public MeetingStatus Status { get; private set; }
    public DateTime? ScheduledAtUtc { get; private set; }
    public int? DurationMinutes { get; private set; }
    public string? Location { get; private set; }
    public Guid? RequestedByEmployeeId { get; private set; }
    public Guid? RequestedByContactId { get; private set; }

    /// <summary>B-5 (M5): the employee who accepted the slot that scheduled this meeting, when one did.</summary>
    public Guid? AcceptedByEmployeeId { get; private set; }
    public DateTime? ReminderSentAtUtc { get; private set; }
    public DateTime? CanceledAtUtc { get; private set; }
    public string? CancelReason { get; private set; }

    private CrmMeeting() { }

    public static Result<CrmMeeting> Create(
        Guid projectId,
        Guid? periodId,
        string title,
        MeetingType type,
        Guid? requestedByEmployeeId,
        Guid? requestedByContactId,
        string? location = null,
        int? durationMinutes = null)
    {
        if (requestedByEmployeeId.HasValue == requestedByContactId.HasValue)
        {
            return CrmMeetingErrors.InvalidRequester;
        }

        return new CrmMeeting
        {
            ProjectId = projectId,
            PeriodId = periodId,
            Title = title,
            Type = type,
            Status = MeetingStatus.Requested,
            RequestedByEmployeeId = requestedByEmployeeId,
            RequestedByContactId = requestedByContactId,
            Location = location,
            DurationMinutes = durationMinutes,
        };
    }

    private bool IsTerminal => Status is MeetingStatus.Completed or MeetingStatus.Canceled or MeetingStatus.NoShow;

    private bool HasStarted(DateTime nowUtc) => ScheduledAtUtc is { } s && nowUtc >= s;

    /// <summary>Set from the accepted slot.</summary>
    /// <param name="acceptedByEmployeeId">B-5 (M5): the employee who accepted the slot, or null when the
    /// client accepted it.</param>
    public Result Schedule(DateTime startsAtUtc, int? durationMinutes, Guid? acceptedByEmployeeId = null)
    {
        if (Status is not (MeetingStatus.Requested or MeetingStatus.Rescheduled))
        {
            return CrmMeetingErrors.InvalidStateTransition;
        }

        Status = MeetingStatus.Scheduled;
        ScheduledAtUtc = startsAtUtc;
        AcceptedByEmployeeId = acceptedByEmployeeId;
        if (durationMinutes is not null)
        {
            DurationMinutes = durationMinutes;
        }

        return Result.Success();
    }

    /// <summary>Reschedule = a new slot goes through the propose/accept loop again.</summary>
    public Result MarkRescheduling(DateTime nowUtc)
    {
        if (IsTerminal)
        {
            return CrmMeetingErrors.InvalidStateTransition;
        }

        if (HasStarted(nowUtc))
        {
            return CrmMeetingErrors.AlreadyStarted;
        }

        Status = MeetingStatus.Rescheduled;
        ScheduledAtUtc = null;
        ReminderSentAtUtc = null;

        // B-6 (M2): the next accepted slot decides how long the meeting lasts — the first schedule's
        // duration no longer applies.
        DurationMinutes = null;
        AcceptedByEmployeeId = null;
        return Result.Success();
    }

    public Result Cancel(string reason, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return CrmMeetingErrors.CancelReasonRequired;
        }

        if (IsTerminal)
        {
            return CrmMeetingErrors.InvalidStateTransition;
        }

        if (HasStarted(nowUtc))
        {
            return CrmMeetingErrors.AlreadyStarted;
        }

        Status = MeetingStatus.Canceled;
        CanceledAtUtc = nowUtc;
        CancelReason = reason;
        return Result.Success();
    }

    public Result Complete(DateTime nowUtc)
    {
        if (Status is not (MeetingStatus.Scheduled or MeetingStatus.Rescheduled) || !HasStarted(nowUtc))
        {
            return CrmMeetingErrors.InvalidStateTransition;
        }

        Status = MeetingStatus.Completed;
        return Result.Success();
    }

    public Result MarkNoShow(DateTime nowUtc)
    {
        if (Status is not (MeetingStatus.Scheduled or MeetingStatus.Rescheduled) || !HasStarted(nowUtc))
        {
            return CrmMeetingErrors.InvalidStateTransition;
        }

        Status = MeetingStatus.NoShow;
        return Result.Success();
    }

    /// <summary>Idempotent — dedupe key already guards the notification, mirrored here only so the
    /// entity's own state can't be driven inconsistent by a retried call.</summary>
    public Result MarkReminderSent(DateTime nowUtc)
    {
        if (ReminderSentAtUtc is not null)
        {
            return Result.Success();
        }

        ReminderSentAtUtc = nowUtc;
        return Result.Success();
    }
}
