using Crm.Contracts.Meetings;
using Notifications.Contracts;
using SharedKernel;
using Xunit;
using MeetingType = Crm.Contracts.MeetingType;

namespace Crm.Application.Tests;

/// <summary>D-8 (B-F1, B-F7): a reschedule tells the employee who had accepted the meeting — the acceptor
/// is cleared in the same save, so the handler can't read it back — and neither notice goes to the person
/// who made the change.</summary>
public sealed partial class MeetingFixTests
{
    [Fact]
    public async Task D8_AClientReschedule_NotifiesTheEmployeeWhoAccepted_AndNotTheClient()
    {
        MeetingProject m = await SeedAsync("d8-resched");
        (Guid acceptorId, string acceptor) = await HireEmployeeAsync("d8-acceptor");
        Result<Guid> requested = await CommandFor<RequestCrmMeetingByClientCommand, Guid>(m.P.ClientAppUserId,
            new RequestCrmMeetingByClientCommand(m.P.ProjectId, m.ContactId, "Site visit", MeetingType.OnSite,
                SlotIn(30).StartsAtUtc, SlotIn(30).EndsAtUtc), readAll: false);
        Assert.True(requested.IsSuccess, requested.Error?.Description);
        Guid meetingId = requested.Value;
        Guid slotId = (await LoadSlotsAsync(meetingId))[0].Id;
        Result accepted = await Command(acceptor, new AcceptCrmMeetingSlotCommand(slotId, acceptorId, acceptor), readAll: true);
        Assert.True(accepted.IsSuccess, accepted.Error?.Description);
        await DrainOutboxAsync();

        CrmMeetingSlotRequest moved = SlotIn(50);
        Result rescheduled = await Command(m.P.ClientAppUserId,
            new RescheduleCrmMeetingByContactCommand(meetingId, m.ContactId, moved.StartsAtUtc, moved.EndsAtUtc), readAll: false);
        Assert.True(rescheduled.IsSuccess, rescheduled.Error?.Description);
        await DrainOutboxAsync();

        Assert.Equal(1, (await NotificationsAsync(acceptor, meetingId)).Count(n => n.Type == NotificationType.CrmMeetingRescheduled));
        Assert.Equal(0, (await NotificationsAsync(m.P.ClientAppUserId, meetingId)).Count(n => n.Type == NotificationType.CrmMeetingRescheduled));
    }

    [Fact]
    public async Task D8_AnEmployeeCancel_NotifiesTheClient_AndNotTheEmployeeWhoCanceled()
    {
        MeetingProject m = await SeedAsync("d8-cancel");
        Guid meetingId = await ProposeAsync(m, SlotIn(30));
        Assert.True((await AcceptAsContactAsync(m, (await LoadSlotsAsync(meetingId))[0].Id)).IsSuccess);

        Assert.True((await CancelAsEmployeeAsync(m, meetingId)).IsSuccess);
        await DrainOutboxAsync();

        Assert.Equal(1, (await NotificationsAsync(m.P.ClientAppUserId, meetingId)).Count(n => n.Type == NotificationType.CrmMeetingCanceled));
        Assert.Equal(0, (await NotificationsAsync(m.P.OwnerAppUserId, meetingId)).Count(n => n.Type == NotificationType.CrmMeetingCanceled));
    }
}
