using BuildingBlocks.Jobs;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Notifications.Contracts;
using Projects.Domain;
using Projects.Infrastructure.Persistence;

namespace Projects.Application.Features.Jobs;

/// <summary>
/// Stage milestone reminders: −5d, −1d, due day, +1d → stage owner + project owner.
///
/// <para><b>"Stage owner" is the head of the stage's active team</b> — a <c>StageTeam</c> row, which is
/// Projects' own table. That is precisely why this job belongs here: <c>StageTeam.HeadId</c> is not on
/// <c>IProjectDirectory</c>'s <c>StageSummary</c> and should not be. Exporting stage-team membership on
/// a public contract so that a job in another module could read it would widen the module's surface for
/// one caller; moving the job to the data costs nothing.</para>
/// </summary>
internal sealed class StageDeadlineJob(
    ProjectsDbContext db,
    IHrDirectory hrDirectory,
    INotificationService notifications,
    TimeProvider timeProvider) : IPeriodicJob
{
    private static readonly int[] Tiers = [-5, -1, 0, 1];

    private static readonly StageStatus[] ActiveStatuses = [StageStatus.Active, StageStatus.Locked];

    public string Name => nameof(StageDeadlineJob);

    public async Task RunAsync(CancellationToken ct)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        DateTime windowStart = now.AddDays(-30);
        DateTime windowEnd = now.AddDays(5);

        // The owner comes with the stage (one join), and only stages of a live project are read.
        var stages = (await db.ProjectStages
            .AsNoTracking()
            .Where(s => s.IsActive
                        && s.EstimatedEndDate != null
                        && ActiveStatuses.Contains(s.Status)
                        && s.EstimatedEndDate >= windowStart && s.EstimatedEndDate <= windowEnd
                        && DeadlineProjects.Live(db).Contains(s.ProjectId))
            .Join(db.Projects.AsNoTracking(), s => s.ProjectId, p => p.Id,
                (s, p) => new { s.Id, s.ProjectId, s.Name, Due = s.EstimatedEndDate!.Value, p.OwnerId })
            .ToListAsync(ct))
            .Select(s => new { Stage = s, Tier = DeadlineTiers.MostRelevant(now, s.Due, Tiers) })
            .Where(x => x.Tier is not null)
            .ToList();
        if (stages.Count == 0)
        {
            return;
        }

        Dictionary<Guid, Guid> stageHeads = await DeadlineProjects.StageHeadsAsync(db, [.. stages.Select(x => x.Stage.Id)], ct);
        Dictionary<Guid, string> users = await DeadlineProjects.AppUsersAsync(
            hrDirectory,
            [.. stages.SelectMany(x => new Guid?[] { stageHeads.TryGetValue(x.Stage.Id, out Guid head) ? head : null, x.Stage.OwnerId })],
            ct);

        foreach (var item in stages)
        {
            var stage = item.Stage;
            int tier = item.Tier!.Value;
            Guid? stageHeadId = stageHeads.TryGetValue(stage.Id, out Guid head) ? head : null;
            var targets = new[] { stageHeadId, stage.OwnerId }
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
                tier > 0 ? NotificationType.StageOverdue : NotificationType.StageDueSoon,
                title: $"Stage {DeadlineTiers.Phrase(tier)} — {stage.Name}",
                body: $"Stage '{stage.Name}' is {DeadlineTiers.Phrase(tier)} (estimated end {stage.Due:yyyy-MM-dd}).",
                entityType: NotificationEntityType.ProjectStage,
                entityId: stage.Id,
                dedupeKey: $"stage-{stage.Id}-due-{stage.Due:yyyyMMdd}-{tier}d",
                projectId: stage.ProjectId,
                ct: ct);
        }
    }
}

/// <summary>Shared by the task and stage deadline jobs: the live-project filter and the batched recipient lookups.</summary>
internal static class DeadlineProjects
{
    /// <summary>Projects whose work still gets reminders: active and Draft or InProgress — the statuses
    /// <see cref="ProjectDeadlineJob"/> reminds on, and the ones that take work writes (NP3).</summary>
    public static IQueryable<Guid> Live(ProjectsDbContext db) => db.Projects.AsNoTracking()
        .Where(p => p.IsActive && (p.Status == ProjectStatus.Draft || p.Status == ProjectStatus.InProgress))
        .Select(p => p.Id);

    /// <summary>Each stage's owner: the head of its first active team (created first, then by id), in one query.</summary>
    public static async Task<Dictionary<Guid, Guid>> StageHeadsAsync(ProjectsDbContext db, List<Guid> stageIds, CancellationToken ct)
    {
        if (stageIds.Count == 0)
        {
            return [];
        }

        Guid[] ids = [.. stageIds.Distinct()];
        var teams = await db.StageTeams.AsNoTracking()
            .Where(t => ids.Contains(t.ProjectStageId) && t.IsActive)
            .OrderBy(t => t.CreatedAtUtc).ThenBy(t => t.Id)
            .Select(t => new { t.ProjectStageId, t.HeadId })
            .ToListAsync(ct);
        return teams.GroupBy(t => t.ProjectStageId).ToDictionary(g => g.Key, g => g.First().HeadId);
    }

    /// <summary>Employee id → app user id for every employee named, in one HR call; employees without a login are
    /// absent.</summary>
    public static async Task<Dictionary<Guid, string>> AppUsersAsync(
        IHrDirectory hrDirectory, List<Guid?> employeeIds, CancellationToken ct)
    {
        Guid[] ids = [.. employeeIds.Where(id => id is not null).Select(id => id!.Value).Distinct()];
        if (ids.Length == 0)
        {
            return [];
        }

        IReadOnlyList<EmployeeSummary> employees = await hrDirectory.GetManyAsync(ids, ct);
        return employees.Where(e => !string.IsNullOrEmpty(e.AppUserId)).ToDictionary(e => e.Id, e => e.AppUserId);
    }
}
