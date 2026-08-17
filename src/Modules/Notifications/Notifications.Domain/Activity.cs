using SharedKernel;

namespace Notifications.Domain;

/// <summary>
/// A human-readable record of something a user did to an entity, shown as a per-entity timeline.
///
/// <para><b>Distinct from <c>AuditLog</c>, and the distinction is worth keeping.</b> An audit row is
/// the technical field-level change trail: derived entirely from EF metadata, written by an interceptor,
/// never phrased by a human. An activity row is a sentence someone reads — "Sara completed the survey" —
/// and it is written deliberately by a handler that knows what the change meant. Deriving one from the
/// other in either direction loses something: the audit trail cannot phrase intent, and the activity
/// feed must not be relied on for completeness.</para>
///
/// <para>The verb is <see cref="ActivityAction"/>, the Domain copy of
/// <c>BuildingBlocks.Auditing.AuditAction</c> — same three members, same values, kept in step by
/// <c>NotificationEnumParityTests</c>. Domain may reference SharedKernel only, so it cannot name the
/// BuildingBlocks enum directly; the duplication is structural, and the parity test is what stops the
/// two copies drifting into disagreement about what kind of change a row describes.</para>
/// </summary>
internal sealed class Activity : Entity
{
    public string? ActorUserId { get; private set; }
    public string? ActorName { get; private set; }

    public ActivityAction Action { get; private set; }
    public string EntityName { get; private set; } = string.Empty;
    public string? EntityId { get; private set; }
    public string? EntityDisplayName { get; private set; }

    /// <summary>Optional grouping key for the project timeline view.</summary>
    public Guid? ProjectId { get; private set; }

    public string Message { get; private set; } = string.Empty;
    public DateTime OccurredAtUtc { get; private set; }

    private Activity()
    {
        // EF materialization.
    }

    public static Activity Create(
        ActivityAction action,
        string entityName,
        string? entityId,
        string message,
        DateTime occurredAtUtc,
        string? actorUserId = null,
        string? actorName = null,
        Guid? projectId = null,
        string? entityDisplayName = null) => new()
        {
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            Message = message,
            OccurredAtUtc = occurredAtUtc,
            ActorUserId = actorUserId,
            ActorName = actorName,
            ProjectId = projectId,
            EntityDisplayName = entityDisplayName,
        };
}
