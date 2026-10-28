using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Crm.Application.Features.Jobs;
using Crm.Contracts.Meetings;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Contracts;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>B-2 (H3/X-8, MT-N6): meeting notifications follow the schedule — a reschedule re-confirms and
/// re-reminds, a reschedule and a cancel are announced, and a stale "scheduled" event sends nothing.</summary>
public sealed partial class MeetingFixTests
{
    private static CrmMeetingSlotRequest SlotIn(int hours) => new(DateTime.UtcNow.AddHours(hours), DateTime.UtcNow.AddHours(hours + 1));

    [Fact]
    public async Task B2_ScheduleRescheduleReaccept_SendsTwoConfirmationsAndTwoReminders_PerRecipient()
    {
        MeetingProject m = await SeedAsync("b2-flow");
        Guid meetingId = await ProposeAsync(m, SlotIn(3), SlotIn(5));
        Assert.True((await AcceptAsContactAsync(m, (await LoadSlotsAsync(meetingId))[0].Id)).IsSuccess);
        await DrainOutboxAsync();
        await RunReminderJobAsync();

        Assert.True((await RescheduleAsEmployeeAsync(m, meetingId, SlotIn(7))).IsSuccess);
        Guid newSlot = (await LoadSlotsAsync(meetingId)).Single(s => s.Status == Domain.MeetingSlotStatus.Proposed && s.ProposedByEmployeeId != null).Id;
        Assert.True((await AcceptAsContactAsync(m, newSlot)).IsSuccess);
        await DrainOutboxAsync();
        await RunReminderJobAsync();

        foreach (string user in new[] { m.P.OwnerAppUserId, m.P.ClientAppUserId })
        {
            IReadOnlyList<NotificationResponse> mine = await NotificationsAsync(user, meetingId);
            Assert.Equal(2, mine.Count(n => n.Type == NotificationType.CrmMeetingScheduled));
            Assert.Equal(2, mine.Count(n => n.Type == NotificationType.CrmMeetingReminder));

            // D-8 (B-F7): the owner made the reschedule, so only the client is told about it.
            Assert.Equal(user == m.P.OwnerAppUserId ? 0 : 1, mine.Count(n => n.Type == NotificationType.CrmMeetingRescheduled));
        }
    }

    /// <summary>D-8 (B-F7) narrows this: the employee who canceled is not told about their own cancel, so
    /// the recipients here are the client only (the D-8 tests cover the actor).</summary>
    [Fact]
    public async Task B2_ACancel_SendsOneCanceledNotificationToEachRecipient()
    {
        MeetingProject m = await SeedAsync("b2-cancel");
        Guid meetingId = await ProposeAsync(m, SlotIn(30));
        Assert.True((await AcceptAsContactAsync(m, (await LoadSlotsAsync(meetingId))[0].Id)).IsSuccess);

        Assert.True((await CancelAsEmployeeAsync(m, meetingId)).IsSuccess);
        await DrainOutboxAsync();

        Assert.Equal(1, (await NotificationsAsync(m.P.ClientAppUserId, meetingId)).Count(n => n.Type == NotificationType.CrmMeetingCanceled));
    }

    /// <summary>MT-N6: the meeting moved before its "scheduled" event was delivered — that confirmation is stale.</summary>
    [Fact]
    public async Task B2_AScheduledEvent_ForAMeetingNoLongerScheduledAtThatTime_SendsNothing()
    {
        MeetingProject m = await SeedAsync("b2-stale");
        Guid meetingId = await ProposeAsync(m, SlotIn(30));
        Assert.True((await AcceptAsContactAsync(m, (await LoadSlotsAsync(meetingId))[0].Id)).IsSuccess);
        Assert.True((await RescheduleAsEmployeeAsync(m, meetingId, SlotIn(50))).IsSuccess);

        await DrainOutboxAsync();

        Assert.Equal(0, (await NotificationsAsync(m.P.ClientAppUserId, meetingId)).Count(n => n.Type == NotificationType.CrmMeetingScheduled));
    }

    private async Task<Result> RescheduleAsEmployeeAsync(MeetingProject m, Guid meetingId, CrmMeetingSlotRequest slot) =>
        await Command(m.P.OwnerAppUserId, new RescheduleCrmMeetingCommand(meetingId, m.P.EmployeeId, m.P.OwnerAppUserId, slot.StartsAtUtc, slot.EndsAtUtc));

    private async Task<Result> CancelAsEmployeeAsync(MeetingProject m, Guid meetingId) =>
        await Command(m.P.OwnerAppUserId, new CancelCrmMeetingCommand(meetingId, m.P.EmployeeId, m.P.OwnerAppUserId, "Client travelling"));

    private async Task RunReminderJobAsync()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        await scope.ServiceProvider.GetServices<BuildingBlocks.Jobs.IPeriodicJob>().OfType<CrmMeetingReminderJob>().Single().RunAsync(CancellationToken.None);
    }

    private async Task<IReadOnlyList<NotificationResponse>> NotificationsAsync(string userId, Guid meetingId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        Result<PagedResult<NotificationResponse>> page = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetMyNotificationsQuery, PagedResult<NotificationResponse>>>()
            .HandleAsync(new GetMyNotificationsQuery(userId, UnreadOnly: false, PageNumber: 1, PageSize: 200));
        Assert.True(page.IsSuccess, page.Error?.Description);
        return [.. page.Value!.Items.Where(n => n.EntityId == meetingId)];
    }
}
