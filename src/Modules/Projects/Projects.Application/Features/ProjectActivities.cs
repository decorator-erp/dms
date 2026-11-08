using BuildingBlocks.Auditing;
using BuildingBlocks.Outbox;
using Notifications.Contracts;
using Projects.Infrastructure.Persistence;

namespace Projects.Application.Features;

/// <summary>
/// S11: the project timeline. Each call stages an <see cref="ActivityRecordedIntegrationEvent"/> on this module's own
/// outbox — call it before the handler's <c>SaveChangesAsync</c>, so the activity is delivered exactly when the change
/// it describes commits (a cross-module write goes on the outbox, never a direct call; Notifications' consumer writes
/// the row and resolves the actor's display name).
/// </summary>
internal static class ProjectActivities
{
    public static void Task(ProjectsDbContext db, string? actorUserId, AuditAction action, Domain.ProjectTask task, string message) =>
        Record(db, actorUserId, action, ActivityEntityNames.ProjectTask, task.Id, task.Name, task.ProjectId, message);

    public static void Stage(ProjectsDbContext db, string? actorUserId, AuditAction action, Domain.ProjectStage stage, string message) =>
        Record(db, actorUserId, action, ActivityEntityNames.ProjectStage, stage.Id, stage.Name, stage.ProjectId, message);

    private static void Record(
        ProjectsDbContext db, string? actorUserId, AuditAction action, string entityName, Guid entityId, string displayName,
        Guid projectId, string message) =>
        db.Outbox().Enqueue(new ActivityRecordedIntegrationEvent(
            Guid.NewGuid(), actorUserId, action, entityName, entityId.ToString(), displayName, projectId, message,
            DateTimeOffset.UtcNow));
}
