using BuildingBlocks.Messaging;
using Crm.Contracts.Clients;
using Crm.Contracts.Meetings;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Contracts;
using SharedKernel;
using Xunit;
using MeetingType = Crm.Contracts.MeetingType;

namespace Crm.Application.Tests;

/// <summary>B-5 (M5) and owner amendment M4: the employee who accepts a client-requested meeting hears about
/// it; a deactivated client's login never does.</summary>
public sealed partial class MeetingFixTests
{
    [Fact]
    public async Task B5_OnAClientRequestedMeeting_TheAcceptingEmployee_IsNotifiedAndReminded()
    {
        MeetingProject m = await SeedAsync("b5-accepting");
        Result<Guid> requested = await CommandFor<RequestCrmMeetingByClientCommand, Guid>(m.P.ClientAppUserId,
            new RequestCrmMeetingByClientCommand(m.P.ProjectId, m.ContactId, "Site visit", MeetingType.OnSite,
                DateTime.UtcNow.AddHours(3), DateTime.UtcNow.AddHours(4)), readAll: false);
        Assert.True(requested.IsSuccess, requested.Error?.Description);
        Guid slotId = (await LoadSlotsAsync(requested.Value))[0].Id;

        Assert.True((await Command(m.P.OwnerAppUserId, new AcceptCrmMeetingSlotCommand(slotId, m.P.EmployeeId, m.P.OwnerAppUserId))).IsSuccess);
        await DrainOutboxAsync();
        await RunReminderJobAsync();

        IReadOnlyList<NotificationResponse> mine = await NotificationsAsync(m.P.OwnerAppUserId, requested.Value);
        Assert.Equal(1, mine.Count(n => n.Type == NotificationType.CrmMeetingScheduled));
        Assert.Equal(1, mine.Count(n => n.Type == NotificationType.CrmMeetingReminder));
        Assert.Equal(m.P.EmployeeId, (await LoadMeetingAsync(requested.Value)).AcceptedByEmployeeId);
    }

    /// <summary>M4: X-3 cut the client off; meeting messages must not reach that login either.</summary>
    [Fact]
    public async Task B5_ADeactivatedClient_GetsNoMeetingNotificationOrReminder()
    {
        MeetingProject m = await SeedAsync("b5-inactive-client");
        Guid meetingId = await ProposeAsync(m, SlotIn(3));
        Assert.True((await AcceptAsContactAsync(m, (await LoadSlotsAsync(meetingId))[0].Id)).IsSuccess);
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<ICommandHandler<SetClientActiveCommand>>()
                .HandleAsync(new SetClientActiveCommand(m.P.ClientId, false))).IsSuccess);
        }

        await DrainOutboxAsync();
        await RunReminderJobAsync();

        Assert.Empty(await NotificationsAsync(m.P.ClientAppUserId, meetingId));
        Assert.NotEmpty(await NotificationsAsync(m.P.OwnerAppUserId, meetingId));
    }
}
