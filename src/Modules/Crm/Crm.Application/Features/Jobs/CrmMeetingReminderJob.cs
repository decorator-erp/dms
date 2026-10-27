using BuildingBlocks.Jobs;
using Crm.Contracts;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Crm.Infrastructure.Services;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notifications.Contracts;
using Projects.Contracts;
using MeetingStatus = Crm.Domain.MeetingStatus;

namespace Crm.Application.Features.Jobs;

/// <summary>
/// Meeting reminders, 24 hours out, to the requesting employee and the project's client.
///
/// <para>Guarded twice, deliberately: <see cref="CrmMeeting.ReminderSentAtUtc"/> keeps the row out of
/// the next scan (cheap — it shrinks the query), and the notification dedupe key makes the send itself
/// idempotent (correct — it survives a crash between notifying and stamping). Either alone would be a
/// bug: without the stamp the query re-reads every upcoming meeting forever, and without the key a
/// crash after the push and before the save re-notifies.</para>
///
/// <para>Recipients cross two module boundaries and both go through published contracts, one hop each
/// — never a chain. The client leg is <c>IProjectDirectory</c> (project → client id) followed by
/// <c>IClientDirectory</c> (client → app user), which is two independent depth-1 calls made by this
/// caller, not an A→B→C traversal.</para>
/// </summary>
internal sealed class CrmMeetingReminderJob(
    CrmDbContext db,
    IHrDirectory hrDirectory,
    IClientDirectory clientDirectory,
    IProjectDirectory projectDirectory,
    INotificationService notifications,
    TimeProvider timeProvider,
    ILogger<CrmMeetingReminderJob> logger) : IPeriodicJob
{
    public string Name => nameof(CrmMeetingReminderJob);

    /// <summary>B-13 (MT-N4): meetings are loaded and their recipients resolved 100 at a time.</summary>
    internal const int BatchSize = 100;

    public async Task RunAsync(CancellationToken ct)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        DateTime horizon = now.AddHours(24);

        List<Guid> dueIds = await Due(now, horizon).OrderBy(m => m.ScheduledAtUtc).Select(m => m.Id).ToListAsync(ct);

        foreach (Guid[] ids in dueIds.Chunk(BatchSize))
        {
            // Re-filtered: a meeting rescheduled or canceled since the id scan is no longer due.
            List<CrmMeeting> batch = await Due(now, horizon).Where(m => ids.Contains(m.Id)).ToListAsync(ct);
            IReadOnlyDictionary<Guid, IReadOnlyList<string>> recipients = await MeetingRecipients.ResolveManyAsync(
                [.. batch.Select(m => new MeetingRecipients.Subject(m.Id, m.ProjectId, m.RequestedByEmployeeId, m.AcceptedByEmployeeId))],
                hrDirectory, projectDirectory, clientDirectory, ct);

            foreach (CrmMeeting meeting in batch)
            {
                // B-13 (MT-N3): one meeting failing (a notification error, a concurrent reschedule) is
                // logged and left unstamped for the next tick; it no longer aborts the rest of the run.
                try
                {
                    await RemindAsync(meeting, recipients[meeting.Id], now, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Meeting reminder failed for meeting {MeetingId}; it is retried on the next run.", meeting.Id);
                    db.Entry(meeting).State = EntityState.Detached;
                }
            }

            db.ChangeTracker.Clear();
        }
    }

    private IQueryable<CrmMeeting> Due(DateTime now, DateTime horizon) =>
        db.CrmMeetings.Where(m => m.IsActive
            // B-11 (L3): a Rescheduled meeting has no time, so only Scheduled can be upcoming.
            && m.Status == MeetingStatus.Scheduled
            && m.ReminderSentAtUtc == null
            && m.ScheduledAtUtc != null
            && m.ScheduledAtUtc > now
            && m.ScheduledAtUtc <= horizon);

    private async Task RemindAsync(CrmMeeting meeting, IReadOnlyList<string> targets, DateTime now, CancellationToken ct)
    {
        DateTime at = meeting.ScheduledAtUtc!.Value;
        if (targets.Count > 0)
        {
            await notifications.NotifyManyAsync(
                [.. targets],
                NotificationType.CrmMeetingReminder,
                title: MeetingTimes.Title("Meeting reminder", meeting.Title),
                body: $"Your meeting is at {MeetingTimes.Display(at)}.",
                entityType: NotificationEntityType.Meeting,
                entityId: meeting.Id,
                dedupeKey: $"crm-meeting-{meeting.Id}-reminder-{MeetingRecipients.Instant(at)}",
                ct: ct);
        }

        // Stamped even when nobody could be resolved: with no recipients there is nothing to send,
        // and leaving the row unstamped would make this job re-scan it every tick until the meeting
        // passes.
        meeting.MarkReminderSent(now);
        await db.SaveChangesAsync(ct);
    }
}
