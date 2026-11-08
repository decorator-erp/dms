using BuildingBlocks.Auditing;
using BuildingBlocks.Events;

namespace Notifications.Contracts;

/// <summary>S11: the <see cref="ActivityEntry.EntityName"/> values the project timeline reads.</summary>
public static class ActivityEntityNames
{
    public const string Project = "Project";
    public const string ProjectStage = "ProjectStage";
    public const string ProjectTask = "ProjectTask";
}

/// <summary>
/// One timeline row. <see cref="Id"/> is the caller's idempotency key: recording the same id twice keeps the first
/// row. <see cref="ActorName"/> may be left null for the recorder to resolve from <see cref="ActorUserId"/> (the
/// login's display name). <see cref="Action"/> is the coarse verb (<see cref="AuditAction.Create"/> for the entity's
/// creation, <see cref="AuditAction.Update"/> for every later change — an edit, a status change, an assignment, a
/// comment or a document); <see cref="Message"/> says what happened.
/// </summary>
public sealed record ActivityEntry(
    Guid Id,
    AuditAction Action,
    string EntityName,
    string? EntityId,
    string Message,
    DateTime OccurredAtUtc,
    string? ActorUserId = null,
    string? ActorName = null,
    Guid? ProjectId = null,
    string? EntityDisplayName = null);

/// <summary>
/// S11: writes activity rows (the human-readable timeline <c>GET /api/activities</c> serves).
///
/// <para><b>Not a cross-module write path.</b> <c>CLAUDE.md</c> allows exactly one synchronous cross-module write,
/// <see cref="INotificationService"/>; this is not a second. Another module records an activity by enqueueing an
/// <see cref="ActivityRecordedIntegrationEvent"/> on its own outbox, in the same transaction as the change it
/// describes, so the row exists exactly when the change committed. Notifications' consumer of that event is the
/// caller of this interface (<c>ActivityRecorderCallerTests</c> holds the line).</para>
///
/// <para>Every contract that returns one thing also returns many: <see cref="RecordManyAsync"/> is the batch
/// sibling, resolving every missing actor name in one directory read.</para>
/// </summary>
public interface IActivityRecorder
{
    Task RecordAsync(ActivityEntry entry, CancellationToken ct);

    /// <summary>Entries whose <see cref="ActivityEntry.Id"/> already exists are skipped, so a redelivery adds
    /// nothing.</summary>
    Task RecordManyAsync(IReadOnlyCollection<ActivityEntry> entries, CancellationToken ct);
}

/// <summary>
/// S11: "something a user did to a project entity", raised by the module that owns the entity (Projects for
/// projects, stages and tasks; Documents for an upload onto one of them) and turned into an activity row by
/// Notifications. IDs and primitives only. <see cref="EventId"/> becomes the activity's id, so the row is written
/// at most once however often the event is delivered. <see cref="EntityName"/> is one of
/// <see cref="ActivityEntityNames"/>; the actor's display name is resolved by the recorder, not carried here.
/// </summary>
public sealed record ActivityRecordedIntegrationEvent(
    Guid EventId,
    string? ActorUserId,
    AuditAction Action,
    string EntityName,
    string? EntityId,
    string? EntityDisplayName,
    Guid? ProjectId,
    string Message,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
