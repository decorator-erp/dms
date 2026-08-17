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
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var windowStart = now.AddDays(-30);
        var windowEnd = now.AddDays(5);

        var stages = await db.ProjectStages
            .AsNoTracking()
            .Where(s => s.IsActive
                        && s.EstimatedEndDate != null
                        && ActiveStatuses.Contains(s.Status)
                        && s.EstimatedEndDate >= windowStart && s.EstimatedEndDate <= windowEnd)
            .Select(s => new { s.Id, s.Name, Due = s.EstimatedEndDate!.Value, s.ProjectId })
            .ToListAsync(ct);

        foreach (var stage in stages)
        {
            if (DeadlineTiers.MostRelevant(now, stage.Due, Tiers) is not { } tier)
                continue;

            var stageHeadId = await db.StageTeams
                .AsNoTracking()
                .Where(t => t.ProjectStageId == stage.Id && t.IsActive)
                .OrderBy(t => t.CreatedAtUtc)
                .Select(t => (Guid?)t.HeadId)
                .FirstOrDefaultAsync(ct);

            var ownerId = await db.Projects
                .AsNoTracking()
                .Where(p => p.Id == stage.ProjectId)
                .Select(p => (Guid?)p.OwnerId)
                .FirstOrDefaultAsync(ct);

            Guid[] employeeIds = [.. new[] { stageHeadId, ownerId }.Where(id => id is not null).Select(id => id!.Value).Distinct()];
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
                tier > 0 ? NotificationType.StageOverdue : NotificationType.StageDueSoon,
                title: $"Stage {DeadlineTiers.Phrase(tier)} — {stage.Name}",
                body: $"Stage '{stage.Name}' is {DeadlineTiers.Phrase(tier)} (estimated end {stage.Due:yyyy-MM-dd}).",
                entityType: NotificationEntityType.ProjectStage,
                entityId: stage.Id,
                dedupeKey: $"stage-{stage.Id}-due-{tier}d",
                ct: ct);
        }
    }
}
