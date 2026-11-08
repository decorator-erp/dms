using BuildingBlocks.Jobs;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Notifications.Contracts;
using Projects.Domain;
using Projects.Infrastructure.Persistence;

namespace Projects.Application.Features.Jobs;

/// <summary>Task due-date reminders: −3d, −1d, due day, +1d → assignee + stage owner.
///
/// <para>A fixed number of queries per run whatever the number of due tasks: the tasks (only those of an active,
/// Draft or InProgress project, as <see cref="ProjectDeadlineJob"/> filters projects), the stage heads of all their
/// stages, and one HR lookup for every recipient. The due date is part of the dedupe key, so moving a deadline
/// reminds again instead of being swallowed by the reminder already sent for the old date.</para></summary>
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
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        DateTime windowStart = now.AddDays(-30); // bound the overdue scan; dedupe already stops repeats.
        DateTime windowEnd = now.AddDays(3);     // earliest tier is -3d.

        var tasks = (await db.ProjectTasks
            .AsNoTracking()
            .Where(t => t.IsActive
                        && t.DueDate != null
                        && ActiveStatuses.Contains(t.Status)
                        && t.DueDate >= windowStart && t.DueDate <= windowEnd
                        && DeadlineProjects.Live(db).Contains(t.ProjectId))
            .Select(t => new { t.Id, t.ProjectId, t.Name, Due = t.DueDate!.Value, t.ProjectStageId, t.AssignedToId })
            .ToListAsync(ct))
            .Select(t => new { Task = t, Tier = DeadlineTiers.MostRelevant(now, t.Due, Tiers) })
            .Where(x => x.Tier is not null)
            .ToList();
        if (tasks.Count == 0)
        {
            return;
        }

        Dictionary<Guid, Guid> stageHeads = await DeadlineProjects.StageHeadsAsync(
            db, [.. tasks.Where(x => x.Task.ProjectStageId != null).Select(x => x.Task.ProjectStageId!.Value)], ct);
        Dictionary<Guid, string> users = await DeadlineProjects.AppUsersAsync(
            hrDirectory,
            [.. tasks.SelectMany(x => new[]
            {
                x.Task.AssignedToId,
                x.Task.ProjectStageId is { } s && stageHeads.TryGetValue(s, out Guid head) ? head : null,
            })],
            ct);

        foreach (var item in tasks)
        {
            var task = item.Task;
            int tier = item.Tier!.Value;
            Guid? stageHeadId = task.ProjectStageId is { } stageId && stageHeads.TryGetValue(stageId, out Guid head) ? head : null;
            var targets = new[] { task.AssignedToId, stageHeadId }
                .Where(id => id is not null && users.ContainsKey(id.Value))
                .Select(id => users[id!.Value])
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (targets.Count == 0)
            {
                continue;
            }

            await notifications.NotifyManyAsync(
                targets,
                tier > 0 ? NotificationType.TaskOverdue : NotificationType.TaskDueSoon,
                title: $"Task {DeadlineTiers.Phrase(tier)} — {task.Name}",
                body: $"Task '{task.Name}' is {DeadlineTiers.Phrase(tier)} (due {task.Due:yyyy-MM-dd}).",
                entityType: NotificationEntityType.ProjectTask,
                entityId: task.Id,
                dedupeKey: $"task-{task.Id}-due-{task.Due:yyyyMMdd}-{tier}d",
                projectId: task.ProjectId,
                ct: ct);
        }
    }
}
