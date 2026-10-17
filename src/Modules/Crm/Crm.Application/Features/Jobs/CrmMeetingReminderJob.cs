using BuildingBlocks.Jobs;
using Crm.Contracts;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
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
    TimeProvider timeProvider) : IPeriodicJob
{
    private static readonly MeetingStatus[] UpcomingStatuses = [MeetingStatus.Scheduled, MeetingStatus.Rescheduled];

    public string Name => nameof(CrmMeetingReminderJob);

    public async Task RunAsync(CancellationToken ct)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        DateTime horizon = now.AddHours(24);

        List<CrmMeeting> upcoming = await db.CrmMeetings
            .Where(m => m.IsActive
                        && UpcomingStatuses.Contains(m.Status)
                        && m.ReminderSentAtUtc == null
                        && m.ScheduledAtUtc != null
                        && m.ScheduledAtUtc > now
                        && m.ScheduledAtUtc <= horizon)
            .ToListAsync(ct);

        foreach (CrmMeeting? meeting in upcoming)
        {
            List<string> targets = await ResolveRecipientsAsync(meeting, ct);

            if (targets.Count > 0)
            {
                await notifications.NotifyManyAsync(
                    targets,
                    NotificationType.CrmMeetingReminder,
                    title: $"Meeting reminder — {meeting.Title}",
                    body: "Your meeting is within the next 24 hours.",
                    entityType: NotificationEntityType.Meeting,
                    entityId: meeting.Id,
                    dedupeKey: $"crm-meeting-{meeting.Id}-reminder",
                    ct: ct);
            }

            // Stamped even when nobody could be resolved: with no recipients there is nothing to send,
            // and leaving the row unstamped would make this job re-scan it every tick until the meeting
            // passes.
            meeting.MarkReminderSent(now);
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<List<string>> ResolveRecipientsAsync(CrmMeeting meeting, CancellationToken ct)
    {
        EmployeeSummary? employee = meeting.RequestedByEmployeeId is { } employeeId
            ? await hrDirectory.GetAsync(employeeId, ct)
            : null;

        ProjectSummary? project = await projectDirectory.GetProjectAsync(meeting.ProjectId, ct);
        ClientSummary? client = project is null ? null : await clientDirectory.GetAsync(project.ClientId, ct);

        string?[] candidates = [employee?.AppUserId, client?.AppUserId];
        return [.. candidates.Where(u => !string.IsNullOrEmpty(u)).Select(u => u!).Distinct(StringComparer.Ordinal)];
    }
}
