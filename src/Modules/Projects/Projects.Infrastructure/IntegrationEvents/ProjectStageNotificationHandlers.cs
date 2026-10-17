using BuildingBlocks.Events;
using Crm.Contracts;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Notifications.Contracts;
using Projects.Contracts;
using Projects.Infrastructure.Persistence;

namespace Projects.Infrastructure.IntegrationEvents;

/// <summary>
/// <b>These live in Projects because "project owner" and "stage team head" are rows in
/// <c>projects.projects</c> and <c>projects.stage_teams</c>.</b>
///
/// <para>Neither is on <c>IProjectDirectory</c> — <c>ProjectSummary</c> has no <c>OwnerId</c> and
/// <c>StageSummary</c> no team — and neither should be. Adding them so a handler in Notifications could
/// read them would export stage-team membership on a public contract that nothing else wants, for one
/// caller. This is the same trade <c>StageDeadlineJob</c> makes for the same reason.</para>
/// </summary>
internal sealed class ProjectStageCompletedNotificationHandler(
    ProjectsDbContext db,
    IHrDirectory hrDirectory,
    IClientDirectory clientDirectory,
    INotificationService notifications) : IIntegrationEventHandler<ProjectStageCompletedIntegrationEvent>
{
    public async Task HandleAsync(ProjectStageCompletedIntegrationEvent @event, CancellationToken ct)
    {
        var project = await db.Projects
            .AsNoTracking()
            .Where(p => p.Id == @event.ProjectId)
            .Select(p => new { p.OwnerId, p.ClientId })
            .FirstOrDefaultAsync(ct);

        if (project is null)
        {
            return;
        }

        EmployeeSummary? owner = await hrDirectory.GetAsync(project.OwnerId, ct);
        ClientSummary? client = await clientDirectory.GetAsync(project.ClientId, ct);

        string?[] candidates = [owner?.AppUserId, client?.AppUserId];
        var targets = candidates.Where(u => !string.IsNullOrEmpty(u)).Select(u => u!)
            .Distinct(StringComparer.Ordinal).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        await notifications.NotifyManyAsync(
            targets,
            NotificationType.StageCompleted,
            title: $"Stage completed — {@event.StageName}",
            body: $"Stage '{@event.StageName}' has been completed.",
            entityType: NotificationEntityType.ProjectStage,
            entityId: @event.StageId,
            dedupeKey: $"stage-{@event.StageId}-completed-{@event.EventId}",
            ct: ct);
    }
}

/// <summary>
/// Rejection goes to the people who have to act on it — the stage's team head and the project owner —
/// and deliberately NOT to the client. A client seeing "your stage was rejected" for an internal
/// quality gate is alarming and actionable by nobody; completion is the milestone worth surfacing to
/// them, which is why the two handlers have different audiences rather than a shared helper.
/// </summary>
internal sealed class ProjectStageRejectedNotificationHandler(
    ProjectsDbContext db,
    IHrDirectory hrDirectory,
    INotificationService notifications) : IIntegrationEventHandler<ProjectStageRejectedIntegrationEvent>
{
    public async Task HandleAsync(ProjectStageRejectedIntegrationEvent @event, CancellationToken ct)
    {
        Guid? ownerId = await db.Projects
            .AsNoTracking()
            .Where(p => p.Id == @event.ProjectId)
            .Select(p => (Guid?)p.OwnerId)
            .FirstOrDefaultAsync(ct);

        Guid? stageHeadId = await db.StageTeams
            .AsNoTracking()
            .Where(t => t.ProjectStageId == @event.StageId && t.IsActive)
            .OrderBy(t => t.CreatedAtUtc)
            .Select(t => (Guid?)t.HeadId)
            .FirstOrDefaultAsync(ct);

        Guid[] employeeIds =
            [.. new[] { ownerId, stageHeadId }.Where(id => id is not null).Select(id => id!.Value).Distinct()];
        if (employeeIds.Length == 0)
        {
            return;
        }

        IReadOnlyList<EmployeeSummary> employees = await hrDirectory.GetManyAsync(employeeIds, ct);
        var targets = employees.Select(e => e.AppUserId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (targets.Count == 0)
        {
            return;
        }

        await notifications.NotifyManyAsync(
            targets,
            NotificationType.StageRejected,
            title: $"Stage rejected — {@event.StageName}",
            body: $"Stage '{@event.StageName}' was rejected and needs rework.",
            entityType: NotificationEntityType.ProjectStage,
            entityId: @event.StageId,
            dedupeKey: $"stage-{@event.StageId}-rejected-{@event.EventId}",
            ct: ct);
    }
}
