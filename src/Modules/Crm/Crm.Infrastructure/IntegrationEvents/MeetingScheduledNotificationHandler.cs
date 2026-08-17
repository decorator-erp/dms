using BuildingBlocks.Events;
using Crm.Contracts;
using Crm.Infrastructure.Persistence;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Notifications.Contracts;
using Projects.Contracts;

namespace Crm.Infrastructure.IntegrationEvents;

/// <summary>
/// Confirms a scheduled meeting to the employee who requested it and to the project's client.
///
/// <para><b>This lives in CRM because the requester is <c>crm.crm_meetings.RequestedByEmployeeId</c>.</b>
/// The payload carries the meeting, project and time, but not who asked — and putting the requester on
/// the event would mean every subscriber gets a field only this one wants.</para>
///
/// <para>Distinct from <c>CrmMeetingReminderJob</c>, which fires 24 hours out and has its own dedupe
/// key. This one confirms the booking at the moment it is made; both can legitimately reach the same
/// person about the same meeting, which is why the keys must not collide — <c>-scheduled</c> here,
/// <c>-reminder</c> there.</para>
/// </summary>
internal sealed class MeetingScheduledNotificationHandler(
    CrmDbContext db,
    IHrDirectory hrDirectory,
    IClientDirectory clientDirectory,
    IProjectDirectory projectDirectory,
    INotificationService notifications) : IIntegrationEventHandler<MeetingScheduledIntegrationEvent>
{
    public async Task HandleAsync(MeetingScheduledIntegrationEvent @event, CancellationToken ct)
    {
        var meeting = await db.CrmMeetings
            .AsNoTracking()
            .Where(m => m.Id == @event.MeetingId)
            .Select(m => new { m.Title, m.RequestedByEmployeeId })
            .FirstOrDefaultAsync(ct);

        if (meeting is null)
            return;

        var employee = meeting.RequestedByEmployeeId is { } employeeId
            ? await hrDirectory.GetAsync(employeeId, ct)
            : null;

        var project = await projectDirectory.GetProjectAsync(@event.ProjectId, ct);
        var client = project is null ? null : await clientDirectory.GetAsync(project.ClientId, ct);

        string?[] candidates = [employee?.AppUserId, client?.AppUserId];
        var targets = candidates.Where(u => !string.IsNullOrEmpty(u)).Select(u => u!)
            .Distinct(StringComparer.Ordinal).ToList();
        if (targets.Count == 0)
            return;

        await notifications.NotifyManyAsync(
            targets,
            NotificationType.CrmMeetingScheduled,
            title: $"Meeting scheduled — {meeting.Title}",
            body: $"The meeting is confirmed for {@event.ScheduledAtUtc:yyyy-MM-dd HH:mm} UTC.",
            entityType: NotificationEntityType.Meeting,
            entityId: @event.MeetingId,
            dedupeKey: $"crm-meeting-{@event.MeetingId}-scheduled",
            ct: ct);
    }
}
