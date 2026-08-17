using BuildingBlocks.Jobs;
using Crm.Contracts;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Notifications.Contracts;
using Projects.Domain;
using Projects.Infrastructure.Persistence;

namespace Projects.Application.Features.Jobs;

/// <summary>
/// Project delivery reminders: −14d, −7d, −3d, −1d, due day, +1d → project owner + client.
///
/// <para><b>This job lives in Projects, not in Notifications.</b> The backend runs all nine deadline
/// jobs inside its notification module, each reading another area's tables. Here that is impossible —
/// Notifications cannot read <c>projects.projects</c> — and the alternative (a read contract per job,
/// with Notifications depending on four modules) would invert the dependency the module exists to
/// avoid. The jobs moved to the data instead. What crosses the boundary is one outbound call to
/// <c>INotificationService</c>, and the recipient lookups, which go through published contracts.</para>
///
/// <para><b>The scan window and status filter are load-bearing for cost, not decoration.</b> Bounding
/// the overdue scan to 30 days keeps this from re-reading every finished project forever; the dedupe
/// key already stops repeats, so the window costs nothing in correctness. Port them; do not
/// "simplify" to a full table scan.</para>
/// </summary>
internal sealed class ProjectDeadlineJob(
    ProjectsDbContext db,
    IHrDirectory hrDirectory,
    IClientDirectory clientDirectory,
    INotificationService notifications,
    TimeProvider timeProvider) : IPeriodicJob
{
    private static readonly int[] Tiers = [-14, -7, -3, -1, 0, 1];

    private static readonly ProjectStatus[] ActiveStatuses = [ProjectStatus.Draft, ProjectStatus.InProgress];

    public string Name => nameof(ProjectDeadlineJob);

    public async Task RunAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var windowStart = now.AddDays(-30); // bound the overdue scan; dedupe already stops repeats.
        var windowEnd = now.AddDays(14);    // earliest tier is -14d.

        var projects = await db.Projects
            .AsNoTracking()
            .Where(p => p.IsActive
                        && p.EstimatedEndDate != null
                        && ActiveStatuses.Contains(p.Status)
                        && p.EstimatedEndDate >= windowStart && p.EstimatedEndDate <= windowEnd)
            .Select(p => new { p.Id, p.Name, Due = p.EstimatedEndDate!.Value, p.OwnerId, p.ClientId })
            .ToListAsync(ct);

        foreach (var project in projects)
        {
            if (DeadlineTiers.MostRelevant(now, project.Due, Tiers) is not { } tier)
                continue;

            var owner = await hrDirectory.GetAsync(project.OwnerId, ct);
            var client = await clientDirectory.GetAsync(project.ClientId, ct);

            string?[] candidates = [owner?.AppUserId, client?.AppUserId];
            var targets = candidates.Where(u => !string.IsNullOrEmpty(u)).Select(u => u!)
                .Distinct(StringComparer.Ordinal).ToList();
            if (targets.Count == 0)
                continue;

            await notifications.NotifyManyAsync(
                targets,
                tier > 0 ? NotificationType.ProjectOverdue : NotificationType.ProjectDueSoon,
                title: $"Project delivery {DeadlineTiers.Phrase(tier)} — {project.Name}",
                body: $"Project '{project.Name}' delivery is {DeadlineTiers.Phrase(tier)} (estimated end {project.Due:yyyy-MM-dd}).",
                entityType: NotificationEntityType.Project,
                entityId: project.Id,
                dedupeKey: $"project-{project.Id}-due-{tier}d",
                ct: ct);
        }
    }
}
