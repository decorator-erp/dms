using BuildingBlocks.Events;
using Crm.Contracts;
using Crm.Infrastructure.Persistence;
using Crm.Infrastructure.Services;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Notifications.Contracts;
using Projects.Contracts;

namespace Crm.Infrastructure.IntegrationEvents;

/// <summary>B-2 (H3/X-8): tells the meeting's recipients it was moved. The key carries the event id: each
/// reschedule is its own announcement, and an outbox retry of the same one still sends once.</summary>
internal sealed class MeetingRescheduledNotificationHandler(
    CrmDbContext db, IHrDirectory hrDirectory, IClientDirectory clientDirectory, IProjectDirectory projectDirectory,
    INotificationService notifications) : IIntegrationEventHandler<MeetingRescheduledIntegrationEvent>
{
    public Task HandleAsync(MeetingRescheduledIntegrationEvent @event, CancellationToken ct) =>
        MeetingChangeNotices.SendAsync(
            db, hrDirectory, clientDirectory, projectDirectory, notifications, @event.MeetingId, @event.ProjectId,
            NotificationType.CrmMeetingRescheduled, "Meeting rescheduled",
            "A new time has been proposed. Please confirm it.", $"crm-meeting-{@event.MeetingId}-rescheduled-{@event.EventId}", ct);
}

/// <summary>B-2 (H3/X-8): tells the meeting's recipients it was canceled.</summary>
internal sealed class MeetingCanceledNotificationHandler(
    CrmDbContext db, IHrDirectory hrDirectory, IClientDirectory clientDirectory, IProjectDirectory projectDirectory,
    INotificationService notifications) : IIntegrationEventHandler<MeetingCanceledIntegrationEvent>
{
    public Task HandleAsync(MeetingCanceledIntegrationEvent @event, CancellationToken ct) =>
        MeetingChangeNotices.SendAsync(
            db, hrDirectory, clientDirectory, projectDirectory, notifications, @event.MeetingId, @event.ProjectId,
            NotificationType.CrmMeetingCanceled, "Meeting canceled",
            "The meeting has been canceled.", $"crm-meeting-{@event.MeetingId}-canceled-{@event.EventId}", ct);
}

internal static class MeetingChangeNotices
{
    public static async Task SendAsync(
        CrmDbContext db, IHrDirectory hrDirectory, IClientDirectory clientDirectory, IProjectDirectory projectDirectory,
        INotificationService notifications, Guid meetingId, Guid projectId, NotificationType type,
        string titlePrefix, string body, string dedupeKey, CancellationToken ct)
    {
        var meeting = await db.CrmMeetings.AsNoTracking()
            .Where(m => m.Id == meetingId)
            .Select(m => new { m.Title, m.RequestedByEmployeeId, m.AcceptedByEmployeeId })
            .FirstOrDefaultAsync(ct);
        if (meeting is null)
        {
            return;
        }

        IReadOnlyList<string> targets = await MeetingRecipients.ResolveAsync(
            projectId, meeting.RequestedByEmployeeId, meeting.AcceptedByEmployeeId, hrDirectory, projectDirectory, clientDirectory, ct);
        if (targets.Count == 0)
        {
            return;
        }

        await notifications.NotifyManyAsync(
            targets, type, title: $"{titlePrefix} — {meeting.Title}", body: body,
            entityType: NotificationEntityType.Meeting, entityId: meetingId, dedupeKey: dedupeKey, ct: ct);
    }
}
