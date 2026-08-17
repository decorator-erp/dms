using BuildingBlocks.Events;
using Hr.Contracts;
using Notifications.Contracts;
using Projects.Contracts;

namespace Notifications.Infrastructure.IntegrationEvents;

/// <summary>
/// The task-lifecycle notifications. These live here rather than in Projects because every one of them
/// reaches its recipient through the payload's <c>AssigneeEmployeeId</c> plus
/// <see cref="IHrDirectory"/> — one already-published contract, no Projects table.
///
/// <para><b>Four of the six lifecycle events notify; two are deliberately silent.</b>
/// <c>ProjectTaskStarted</c> and <c>ProjectTaskCancelled</c> are the silent pair: starting a task is
/// something the assignee just did, and telling someone what they themselves did a moment ago is noise
/// that trains people to ignore the badge. Cancellation is an administrative act the assignee sees on
/// their board. Assigned / Completed / Rejected / Reopened all carry information the assignee does not
/// otherwise have.</para>
///
/// <para>Every handler passes a dedupe key, per the rule on <c>NotificationService</c>: a handler runs
/// inside its own module's transaction, not this one's, so an outbox retry after a downstream failure
/// would otherwise duplicate.</para>
/// </summary>
internal sealed class ProjectTaskAssignedNotificationHandler(
    IHrDirectory hrDirectory,
    INotificationService notifications) : IIntegrationEventHandler<ProjectTaskAssignedIntegrationEvent>
{
    public async Task HandleAsync(ProjectTaskAssignedIntegrationEvent @event, CancellationToken ct)
    {
        var assignee = await hrDirectory.GetAsync(@event.AssigneeEmployeeId, ct);
        if (string.IsNullOrEmpty(assignee?.AppUserId))
            return;

        await notifications.NotifyAsync(
            assignee.AppUserId,
            NotificationType.TaskAssigned,
            title: $"Task assigned — {@event.TaskName}",
            body: $"'{@event.TaskName}' has been assigned to you.",
            entityType: NotificationEntityType.ProjectTask,
            entityId: @event.TaskId,
            dedupeKey: $"task-{@event.TaskId}-assigned-{@event.EventId}",
            ct: ct);
    }
}

internal sealed class ProjectTaskCompletedNotificationHandler(
    IHrDirectory hrDirectory,
    INotificationService notifications) : IIntegrationEventHandler<ProjectTaskCompletedIntegrationEvent>
{
    public Task HandleAsync(ProjectTaskCompletedIntegrationEvent @event, CancellationToken ct) =>
        TaskNotifier.NotifyAssigneeAsync(
            hrDirectory, notifications, @event.AssigneeEmployeeId, @event.TaskId, NotificationType.TaskCompleted,
            $"Task completed — {@event.TaskName}", $"'{@event.TaskName}' has been marked complete.",
            $"task-{@event.TaskId}-completed-{@event.EventId}", ct);
}

internal sealed class ProjectTaskRejectedNotificationHandler(
    IHrDirectory hrDirectory,
    INotificationService notifications) : IIntegrationEventHandler<ProjectTaskRejectedIntegrationEvent>
{
    public Task HandleAsync(ProjectTaskRejectedIntegrationEvent @event, CancellationToken ct) =>
        TaskNotifier.NotifyAssigneeAsync(
            hrDirectory, notifications, @event.AssigneeEmployeeId, @event.TaskId, NotificationType.TaskRejected,
            $"Task rejected — {@event.TaskName}", $"'{@event.TaskName}' was rejected and needs rework.",
            $"task-{@event.TaskId}-rejected-{@event.EventId}", ct);
}

internal sealed class ProjectTaskReopenedNotificationHandler(
    IHrDirectory hrDirectory,
    INotificationService notifications) : IIntegrationEventHandler<ProjectTaskReopenedIntegrationEvent>
{
    public Task HandleAsync(ProjectTaskReopenedIntegrationEvent @event, CancellationToken ct) =>
        TaskNotifier.NotifyAssigneeAsync(
            hrDirectory, notifications, @event.AssigneeEmployeeId, @event.TaskId, NotificationType.TaskReopened,
            $"Task reopened — {@event.TaskName}", $"'{@event.TaskName}' has been reopened.",
            $"task-{@event.TaskId}-reopened-{@event.EventId}", ct);
}

/// <summary>The one shared step: resolve an optional employee id to a user id, skip if it resolves to
/// nobody. Three handlers differ only in wording and type, and repeating the resolution in each is how
/// one of them eventually stops checking for null.</summary>
internal static class TaskNotifier
{
    internal static async Task NotifyAssigneeAsync(
        IHrDirectory hrDirectory,
        INotificationService notifications,
        Guid? assigneeEmployeeId,
        Guid taskId,
        NotificationType type,
        string title,
        string body,
        string dedupeKey,
        CancellationToken ct)
    {
        if (assigneeEmployeeId is not { } employeeId)
            return;

        var assignee = await hrDirectory.GetAsync(employeeId, ct);
        if (string.IsNullOrEmpty(assignee?.AppUserId))
            return;

        await notifications.NotifyAsync(
            assignee.AppUserId, type, title, body,
            NotificationEntityType.ProjectTask, taskId, dedupeKey, ct);
    }
}
