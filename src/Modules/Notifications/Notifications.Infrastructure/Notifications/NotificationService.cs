using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notifications.Contracts;
using Notifications.Domain;
using Notifications.Infrastructure.Persistence;
using Npgsql;
using ContractsEntityType = Notifications.Contracts.NotificationEntityType;
using ContractsType = Notifications.Contracts.NotificationType;
using DomainEntityType = Notifications.Domain.NotificationEntityType;
using DomainType = Notifications.Domain.NotificationType;

namespace Notifications.Infrastructure.Notifications;

/// <summary>
/// Persists notification rows and pushes them over SignalR. <b>A failed push never throws</b> — the row
/// is the source of truth and real time is best-effort, so a dropped WebSocket must not fail the job or
/// handler that raised the notification.
///
/// <para><b>The transaction boundary, stated because it is not the obvious one.</b> Integration-event
/// handlers run inside <c>InboxDedupeDecorator</c>'s explicit transaction — on <i>their own</i> module's
/// <c>DbContext</c>. This service writes through <c>NotificationsDbContext</c>, a different context with
/// its own connection, so its commit is independent: if a handler fails after notifying, its work rolls
/// back and the notification row survives.</para>
///
/// <para>That is deliberate rather than overlooked, and it has a rule attached: <b>an
/// integration-event handler that notifies must pass a dedupe key.</b> With one, the outbox's retry
/// re-runs the handler and this service silently skips the already-delivered row — the reader sees one
/// notification, at most slightly early. Without one, the retry duplicates. Making the notification
/// join the handler's transaction would mean writing it through the handler's own context, which would
/// put <c>notify</c> tables in seven modules' models and undo the whole point of the boundary.</para>
///
/// <para>Periodic jobs have no ambient transaction at all, so for them the per-recipient save below is
/// simply the commit.</para>
/// </summary>
internal sealed class NotificationService(
    NotificationsDbContext dbContext,
    IHubContext<NotificationsHub> hubContext,
    ILogger<NotificationService> logger) : INotificationService
{
    public Task NotifyAsync(
        string recipientUserId,
        ContractsType type,
        string title,
        string? body = null,
        ContractsEntityType? entityType = null,
        Guid? entityId = null,
        string? dedupeKey = null,
        CancellationToken ct = default)
        => NotifyManyAsync([recipientUserId], type, title, body, entityType, entityId, dedupeKey, ct);

    public async Task NotifyManyAsync(
        IReadOnlyCollection<string> recipientUserIds,
        ContractsType type,
        string title,
        string? body = null,
        ContractsEntityType? entityType = null,
        Guid? entityId = null,
        string? dedupeKey = null,
        CancellationToken ct = default)
    {
        foreach (string? recipientUserId in recipientUserIds.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal))
        {
            // The SCOPED key -- "{logicalKey}:{recipientUserId}". Computed by the entity so this service
            // cannot scope it differently from Notification.Create, which is what actually stores it.
            string? storedKey = Notification.ScopeDedupeKey(dedupeKey, recipientUserId);

            if (storedKey is not null && await dbContext.Notifications.AnyAsync(n => n.DedupeKey == storedKey, ct))
            {
                continue; // already delivered to this recipient - silently skip.
            }

            var notification = Notification.Create(
                recipientUserId, MapType(type), title, body, MapEntityType(entityType), entityId, dedupeKey);

            dbContext.Notifications.Add(notification);

            try
            {
                // Saved per recipient, not once at the end, and that is load-bearing: the check above is
                // a read, so two processes can both pass it. The unique index is what actually arbitrates,
                // and it can only do so one row at a time -- a batched save would fail the whole fan-out
                // when any single recipient collided.
                await dbContext.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (
                storedKey is not null && ex.InnerException is PostgresException { SqlState: "23505" })
            {
                // Lost the race on the dedupe key: someone else delivered it. Detach so the failed
                // insert is not retried by the next recipient's SaveChanges, and carry on.
                dbContext.Entry(notification).State = EntityState.Detached;
                continue;
            }

            await PushAsync(notification, ct);
        }
    }

    private async Task PushAsync(Notification notification, CancellationToken ct)
    {
        try
        {
            int unreadCount = await dbContext.Notifications.CountAsync(
                n => n.RecipientUserId == notification.RecipientUserId && n.ReadAtUtc == null && n.IsActive, ct);

            await hubContext.Clients
                .Group(NotificationsHub.UserGroup(notification.RecipientUserId))
                .SendAsync(NotificationsHub.ReceiveMethod, new
                {
                    id = notification.Id,
                    type = notification.Type,
                    title = notification.Title,
                    body = notification.Body,
                    entityType = notification.EntityType,
                    entityId = notification.EntityId,
                    createdAtUtc = notification.CreatedAtUtc,
                    unreadCount,
                }, ct);
        }
        catch (Exception ex)
        {
            // Never rethrow. The row is committed; a caller must not be failed by a transport problem.
            logger.LogWarning(ex,
                "Real-time push failed for notification {NotificationId} to user {UserId} — the row is persisted.",
                notification.Id, notification.RecipientUserId);
        }
    }

    // Contracts -> Domain. The two copies are asserted identical in name and value by
    // NotificationEnumParityTests, so the cast is total; it is a cast rather than a switch precisely
    // because that test is what guarantees totality.
    private static DomainType MapType(ContractsType type) => (DomainType)(int)type;

    private static DomainEntityType? MapEntityType(ContractsEntityType? entityType) =>
        entityType is null ? null : (DomainEntityType)(int)entityType.Value;
}
