using Crm.Domain;
using Xunit;

namespace Crm.UnitTests;

public class CrmMeetingTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static CrmMeeting CreateMeeting(Guid? employeeId = null, Guid? contactId = null) =>
        CrmMeeting.Create(
            Guid.NewGuid(), null, "Kickoff", (MeetingType)1,
            employeeId ?? Guid.NewGuid(), contactId).Value!;

    [Fact]
    public void Create_WithEmployeeRequester_Succeeds()
    {
        var result = CrmMeeting.Create(Guid.NewGuid(), null, "Kickoff", (MeetingType)1, Guid.NewGuid(), null);

        Assert.True(result.IsSuccess);
        Assert.Equal(MeetingStatus.Requested, result.Value!.Status);
    }

    [Fact]
    public void Create_WithBothRequesters_Fails()
    {
        var result = CrmMeeting.Create(
            Guid.NewGuid(), null, "Kickoff", (MeetingType)1, Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(CrmMeetingErrors.InvalidRequester, result.Error);
    }

    [Fact]
    public void Create_WithNeitherRequester_Fails()
    {
        var result = CrmMeeting.Create(Guid.NewGuid(), null, "Kickoff", (MeetingType)1, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmMeetingErrors.InvalidRequester, result.Error);
    }

    [Fact]
    public void Schedule_FromRequested_Succeeds()
    {
        var meeting = CreateMeeting();

        var result = meeting.Schedule(Now.AddDays(1), 60);

        Assert.True(result.IsSuccess);
        Assert.Equal(MeetingStatus.Scheduled, meeting.Status);
        Assert.Equal(Now.AddDays(1), meeting.ScheduledAtUtc);
    }

    [Fact]
    public void Schedule_FromCompleted_Fails()
    {
        var meeting = CreateMeeting();
        Assert.True(meeting.Schedule(Now.AddHours(-1), 60).IsSuccess);
        Assert.True(meeting.Complete(Now).IsSuccess);

        var result = meeting.Schedule(Now.AddDays(1), 60);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmMeetingErrors.InvalidStateTransition, result.Error);
    }

    [Fact]
    public void MarkRescheduling_ClearsScheduledTime()
    {
        var meeting = CreateMeeting();
        Assert.True(meeting.Schedule(Now.AddDays(1), 60).IsSuccess);

        var result = meeting.MarkRescheduling(Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(MeetingStatus.Rescheduled, meeting.Status);
        Assert.Null(meeting.ScheduledAtUtc);
    }

    [Fact]
    public void MarkRescheduling_AfterStart_Fails()
    {
        var meeting = CreateMeeting();
        Assert.True(meeting.Schedule(Now.AddHours(-1), 60).IsSuccess);

        var result = meeting.MarkRescheduling(Now);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmMeetingErrors.AlreadyStarted, result.Error);
    }

    [Fact]
    public void Cancel_WithoutReason_Fails()
    {
        var meeting = CreateMeeting();

        var result = meeting.Cancel("", Now);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmMeetingErrors.CancelReasonRequired, result.Error);
    }

    [Fact]
    public void Cancel_BeforeStart_Succeeds()
    {
        var meeting = CreateMeeting();
        Assert.True(meeting.Schedule(Now.AddDays(1), 60).IsSuccess);

        var result = meeting.Cancel("client asked", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(MeetingStatus.Canceled, meeting.Status);
        Assert.Equal("client asked", meeting.CancelReason);
    }

    [Fact]
    public void Cancel_AfterStart_Fails()
    {
        var meeting = CreateMeeting();
        Assert.True(meeting.Schedule(Now.AddHours(-1), 60).IsSuccess);

        var result = meeting.Cancel("client asked", Now);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmMeetingErrors.AlreadyStarted, result.Error);
    }

    [Fact]
    public void Complete_AfterStart_Succeeds()
    {
        var meeting = CreateMeeting();
        Assert.True(meeting.Schedule(Now.AddHours(-1), 60).IsSuccess);

        var result = meeting.Complete(Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(MeetingStatus.Completed, meeting.Status);
    }

    [Fact]
    public void Complete_BeforeStart_Fails()
    {
        var meeting = CreateMeeting();
        Assert.True(meeting.Schedule(Now.AddDays(1), 60).IsSuccess);

        var result = meeting.Complete(Now);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmMeetingErrors.InvalidStateTransition, result.Error);
    }

    [Fact]
    public void MarkNoShow_AfterStart_Succeeds()
    {
        var meeting = CreateMeeting();
        Assert.True(meeting.Schedule(Now.AddHours(-1), 60).IsSuccess);

        var result = meeting.MarkNoShow(Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(MeetingStatus.NoShow, meeting.Status);
    }

    [Fact]
    public void MarkNoShow_BeforeStart_Fails()
    {
        var meeting = CreateMeeting();
        Assert.True(meeting.Schedule(Now.AddDays(1), 60).IsSuccess);

        var result = meeting.MarkNoShow(Now);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmMeetingErrors.InvalidStateTransition, result.Error);
    }

    [Fact]
    public void Complete_FromCanceled_Fails()
    {
        var meeting = CreateMeeting();
        Assert.True(meeting.Schedule(Now.AddDays(1), 60).IsSuccess);
        Assert.True(meeting.Cancel("client asked", Now).IsSuccess);

        var result = meeting.Complete(Now.AddDays(1));

        Assert.True(result.IsFailure);
        Assert.Equal(CrmMeetingErrors.InvalidStateTransition, result.Error);
    }

    [Fact]
    public void Cancel_WhenAlreadyCanceled_Fails()
    {
        var meeting = CreateMeeting();
        Assert.True(meeting.Schedule(Now.AddDays(1), 60).IsSuccess);
        Assert.True(meeting.Cancel("client asked", Now).IsSuccess);

        var result = meeting.Cancel("client asked again", Now);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmMeetingErrors.InvalidStateTransition, result.Error);
    }

    [Fact]
    public void Schedule_FromScheduled_Fails()
    {
        var meeting = CreateMeeting();
        Assert.True(meeting.Schedule(Now.AddDays(1), 60).IsSuccess);

        var result = meeting.Schedule(Now.AddDays(2), 60);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmMeetingErrors.InvalidStateTransition, result.Error);
    }

    [Fact]
    public void MarkReminderSent_IsIdempotent()
    {
        var meeting = CreateMeeting();

        Assert.True(meeting.MarkReminderSent(Now).IsSuccess);
        var firstStamp = meeting.ReminderSentAtUtc;

        Assert.True(meeting.MarkReminderSent(Now.AddHours(1)).IsSuccess);

        Assert.Equal(firstStamp, meeting.ReminderSentAtUtc);
    }
}
