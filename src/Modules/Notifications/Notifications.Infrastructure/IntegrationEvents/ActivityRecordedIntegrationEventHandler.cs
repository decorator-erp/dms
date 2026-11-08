using BuildingBlocks.Events;
using Notifications.Contracts;
using Notifications.Infrastructure.Activities;

namespace Notifications.Infrastructure.IntegrationEvents;

/// <summary>S11: turns another module's <see cref="ActivityRecordedIntegrationEvent"/> into an activity row. The
/// event id is the row id (<see cref="ActivityRecorder"/> skips an id it already has), so a redelivery that gets
/// past the inbox still writes nothing twice.</summary>
internal sealed class ActivityRecordedIntegrationEventHandler(IActivityRecorder recorder)
    : IIntegrationEventHandler<ActivityRecordedIntegrationEvent>
{
    public Task HandleAsync(ActivityRecordedIntegrationEvent @event, CancellationToken ct) =>
        recorder.RecordAsync(
            new ActivityEntry(
                @event.EventId, @event.Action, @event.EntityName, @event.EntityId, @event.Message,
                @event.OccurredAtUtc.UtcDateTime, @event.ActorUserId, ActorName: null, @event.ProjectId,
                @event.EntityDisplayName),
            ct);
}
