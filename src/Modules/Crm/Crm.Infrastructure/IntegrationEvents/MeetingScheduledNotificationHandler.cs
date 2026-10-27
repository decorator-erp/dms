using BuildingBlocks.Events;
using Crm.Contracts;
using Crm.Infrastructure.Persistence;
using Crm.Infrastructure.Services;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
    INotificationService notifications,
    IOptions<OfficeHoursOptions> officeHours) : IIntegrationEventHandler<MeetingScheduledIntegrationEvent>
{
    public async Task HandleAsync(MeetingScheduledIntegrationEvent @event, CancellationToken ct)
    {
        var meeting = await db.CrmMeetings
            .AsNoTracking()
            .Where(m => m.Id == @event.MeetingId)
            .Select(m => new { m.Title, m.RequestedByEmployeeId, m.AcceptedByEmployeeId, m.Status, m.ScheduledAtUtc })
            .FirstOrDefaultAsync(ct);

        // MT-N6: the current state wins. A meeting canceled or moved since this event was raised is not
        // confirmed for the old time; its own events tell the recipients what happened instead.
        if (meeting is null || meeting.Status != Domain.MeetingStatus.Scheduled || meeting.ScheduledAtUtc != @event.ScheduledAtUtc)
        {
            return;
        }

        IReadOnlyList<string> targets = await MeetingRecipients.ResolveAsync(
            @event.ProjectId, meeting.RequestedByEmployeeId, meeting.AcceptedByEmployeeId, hrDirectory, projectDirectory, clientDirectory, ct);
        if (targets.Count == 0)
        {
            return;
        }

        await notifications.NotifyManyAsync(
            targets,
            NotificationType.CrmMeetingScheduled,
            title: MeetingTimes.Title("Meeting scheduled", meeting.Title),
            body: $"The meeting is confirmed for {MeetingTimes.Display(@event.ScheduledAtUtc, officeHours.Value.TimezoneId)}.",
            entityType: NotificationEntityType.Meeting,
            entityId: @event.MeetingId,
            dedupeKey: $"crm-meeting-{@event.MeetingId}-scheduled-{MeetingRecipients.Instant(@event.ScheduledAtUtc)}",
            ct: ct);
    }
}
