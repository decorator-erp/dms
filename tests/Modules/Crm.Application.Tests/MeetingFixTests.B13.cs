using Crm.Application.Features.Jobs;
using Crm.Contracts;
using Crm.Infrastructure.Persistence;
using Crm.Infrastructure.Services;
using Hr.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Notifications.Contracts;
using Projects.Contracts;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>B-13 (MT-N3, MT-N4): one failing meeting no longer aborts the reminder run, and the reminder
/// says when the meeting is.</summary>
public sealed partial class MeetingFixTests
{
    [Fact]
    public async Task B13_OneFailingReminder_DoesNotStopTheOthers_AndIsRetriedNextRun()
    {
        MeetingProject m = await SeedAsync("b13-isolate");
        Guid failing = await ScheduleWithinADayAsync(m, 3);
        Guid healthy = await ScheduleWithinADayAsync(m, 5);

        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            IServiceProvider sp = scope.ServiceProvider;
            var job = new CrmMeetingReminderJob(
                sp.GetRequiredService<CrmDbContext>(), sp.GetRequiredService<IHrDirectory>(),
                sp.GetRequiredService<IClientDirectory>(), sp.GetRequiredService<IProjectDirectory>(),
                new FailingFor(failing, sp.GetRequiredService<INotificationService>()),
                TimeProvider.System, NullLogger<CrmMeetingReminderJob>.Instance);

            await job.RunAsync(CancellationToken.None);
        }

        Assert.NotNull((await LoadMeetingAsync(healthy)).ReminderSentAtUtc);
        Assert.Null((await LoadMeetingAsync(failing)).ReminderSentAtUtc);

        await RunReminderJobAsync();
        Assert.NotNull((await LoadMeetingAsync(failing)).ReminderSentAtUtc);
    }

    [Fact]
    public async Task B13_TheReminder_StatesTheMeetingTime()
    {
        MeetingProject m = await SeedAsync("b13-body");
        Guid meetingId = await ScheduleWithinADayAsync(m, 4);
        DateTime at = (await LoadMeetingAsync(meetingId)).ScheduledAtUtc!.Value;

        await RunReminderJobAsync();

        NotificationResponse reminder = Assert.Single(await NotificationsAsync(m.P.ClientAppUserId, meetingId),
            n => n.Type == NotificationType.CrmMeetingReminder);
        Assert.Contains(MeetingTimes.Display(at), reminder.Body, StringComparison.Ordinal);
    }

    private async Task<Guid> ScheduleWithinADayAsync(MeetingProject m, int hours)
    {
        Guid meetingId = await ProposeAsync(m, SlotIn(hours));
        Assert.True((await AcceptAsContactAsync(m, (await LoadSlotsAsync(meetingId))[0].Id)).IsSuccess);
        return meetingId;
    }

    /// <summary>Throws for one meeting's notifications and delegates the rest to the real service.</summary>
    private sealed class FailingFor(Guid meetingId, INotificationService inner) : INotificationService
    {
        public Task NotifyAsync(string recipientUserId, NotificationType type, string title, string? body = null,
            NotificationEntityType? entityType = null, Guid? entityId = null, string? dedupeKey = null, CancellationToken ct = default) =>
            NotifyManyAsync([recipientUserId], type, title, body, entityType, entityId, dedupeKey, ct);

        public Task NotifyManyAsync(IReadOnlyCollection<string> recipientUserIds, NotificationType type, string title, string? body = null,
            NotificationEntityType? entityType = null, Guid? entityId = null, string? dedupeKey = null, CancellationToken ct = default) =>
            entityId == meetingId
                ? throw new InvalidOperationException("Simulated notification failure.")
                : inner.NotifyManyAsync(recipientUserIds, type, title, body, entityType, entityId, dedupeKey, ct);
    }
}
