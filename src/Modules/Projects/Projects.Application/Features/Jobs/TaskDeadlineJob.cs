using BuildingBlocks.Jobs;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Notifications.Contracts;
using Projects.Domain;
using Projects.Infrastructure.Persistence;

namespace Projects.Application.Features.Jobs;

/// <summary>Task due-date reminders: −3d, −1d, due day, +1d → assignee + stage owner.</summary>
internal sealed class TaskDeadlineJob(
    ProjectsDbContext db,
    IHrDirectory hrDirectory,
    INotificationService notifications,
    TimeProvider timeProvider) : IPeriodicJob
{
    private static readonly int[] Tiers = [-3, -1, 0, 1];

    private static readonly ProjectTaskStatus[] ActiveStatuses =
        [ProjectTaskStatus.Open, ProjectTaskStatus.InProgress, ProjectTaskStatus.PendingApproval, ProjectTaskStatus.Rejected];

    public string Name => nameof(TaskDeadlineJob);

    public async Task RunAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var windowStart = now.AddDays(-30); // bound the overdue scan; dedupe already stops repeats.
        var windowEnd = now.AddDays(3);     // earliest tier is -3d.

        var tasks = await db.ProjectTasks
            .AsNoTracking()
            .Where(t => t.IsActive
                        && t.DueDate != null
                        && ActiveStatuses.Contains(t.Status)
                        && t.DueDate >= windowStart && t.DueDate <= windowEnd)
            .Select(t => new { t.Id, t.Name, Due = t.DueDate!.Value, t.ProjectStageId, t.AssignedToId })
            .ToListAsync(ct);

        foreach (var task in tasks)
        {
            if (DeadlineTiers.MostRelevant(now, task.Due, Tiers) is not { } tier)
                continue;

            Guid? stageHeadId = null;
            if (task.ProjectStageId is { } stageId)
            {
                stageHeadId = await db.StageTeams
                    .AsNoTracking()
                    .Where(t => t.ProjectStageId == stageId && t.IsActive)
                    .OrderBy(t => t.CreatedAtUtc)
                    .Select(t => (Guid?)t.HeadId)
                    .FirstOrDefaultAsync(ct);
            }

            Guid[] employeeIds =
                [.. new[] { task.AssignedToId, stageHeadId }.Where(id => id is not null).Select(id => id!.Value).Distinct()];
            if (employeeIds.Length == 0)
                continue;

            var employees = await hrDirectory.GetManyAsync(employeeIds, ct);
            var targets = employees.Select(e => e.AppUserId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (targets.Count == 0)
                continue;

            await notifications.NotifyManyAsync(
                targets,
                tier > 0 ? NotificationType.TaskOverdue : NotificationType.TaskDueSoon,
                title: $"Task {DeadlineTiers.Phrase(tier)} — {task.Name}",
                body: $"Task '{task.Name}' is {DeadlineTiers.Phrase(tier)} (due {task.Due:yyyy-MM-dd}).",
                entityType: NotificationEntityType.ProjectTask,
                entityId: task.Id,
                dedupeKey: $"task-{task.Id}-due-{tier}d",
                ct: ct);
        }
    }
}
