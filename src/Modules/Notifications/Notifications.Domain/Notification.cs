using SharedKernel;

namespace Notifications.Domain;

/// <summary>
/// One in-app notification row for one recipient. Created by integration-event handlers and periodic
/// jobs — never composed inline by business logic — and pushed over SignalR as a best-effort extra:
/// the row is the source of truth, real-time delivery is not.
///
/// <para>Deliberately <b>not</b> <c>IAuditable</c>. Auditing notification rows would record the audit
/// trail's own neighbours: a field-level diff of "a notification was created" adds nothing the row
/// itself does not already say, and it would roughly double audit volume for the noisiest table in the
/// system.</para>
/// </summary>
internal sealed class Notification : Entity
{
    /// <summary>Identity user id (string, per ASP.NET Identity), not an HR employee id — notifications
    /// target accounts, and not every recipient has an employee record.</summary>
    public string RecipientUserId { get; private set; } = string.Empty;

    public NotificationType Type { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Body { get; private set; }

    /// <summary>Deep-link target, paired with <see cref="EntityId"/>.</summary>
    public NotificationEntityType? EntityType { get; private set; }
    public Guid? EntityId { get; private set; }

    /// <summary>Null means unread.</summary>
    public DateTime? ReadAtUtc { get; private set; }

    /// <summary>The <b>scoped</b> key — always <c>"{logicalKey}:{recipientUserId}"</c>, never the
    /// caller's logical key on its own. See <see cref="ScopeDedupeKey"/>.</summary>
    public string? DedupeKey { get; private set; }

    public bool IsRead => ReadAtUtc is not null;

    private Notification()
    {
        // EF materialization.
    }

    /// <summary>
    /// <b>The per-recipient dedupe rule, in the domain rather than in the service that happens to call
    /// it first.</b>
    ///
    /// <para>A logical key like <c>"task-{id}-due--1d"</c> names one <i>reminder</i>, which may fan out
    /// to several people; the stored key names one reminder <i>to one person</i>. Scoping by recipient
    /// is what lets the unique index enforce "each user gets this at most once" while still allowing
    /// three users to receive the same reminder — and it is therefore what makes a periodic job safe to
    /// re-run after a restart, an overlapping tick, or a manual flush. The whole consolidated-sweep
    /// design rests on this one line.</para>
    ///
    /// <para>Stored on the entity so it can be unit-tested and so no future second caller can scope it
    /// differently. A null logical key means "always deliver" and stays null: the unique index is
    /// filtered on <c>dedupe_key IS NOT NULL</c>, so unkeyed notifications never collide.</para>
    /// </summary>
    public static string? ScopeDedupeKey(string? logicalKey, string recipientUserId) =>
        logicalKey is null ? null : $"{logicalKey}:{recipientUserId}";

    public static Notification Create(
        string recipientUserId,
        NotificationType type,
        string title,
        string? body = null,
        NotificationEntityType? entityType = null,
        Guid? entityId = null,
        string? dedupeKey = null) => new()
        {
            RecipientUserId = recipientUserId,
            Type = type,
            Title = title,
            Body = body,
            EntityType = entityType,
            EntityId = entityId,
            DedupeKey = ScopeDedupeKey(dedupeKey, recipientUserId),
        };

    /// <summary>Idempotent — marking an already-read notification keeps the original read time, so a
    /// double click or a retried request never rewrites when the user actually saw it.</summary>
    public void MarkRead(DateTime nowUtc) => ReadAtUtc ??= nowUtc;
}
