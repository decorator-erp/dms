namespace Notifications.Contracts;

/// <summary>
/// Creates in-app notification rows and pushes them in real time (SignalR). Called from integration-event
/// handlers and periodic jobs — never inline from business logic.
///
/// <para><b>This is the one allowed outbound direction across the module boundary.</b> Seven modules
/// reference <c>Notifications.Contracts</c> so their jobs and handlers can call this; nothing in
/// Notifications references them back. <c>CLAUDE.md</c> says cross-module writes go on the outbox and
/// never by direct call — this is a deliberate, recorded exception, because routing a notification
/// through the outbox would add a commit and a delivery hop to produce a row that is itself the
/// notification. See <c>docs/adding-a-module.md</c>.</para>
///
/// <para>Signature ported verbatim from
/// <c>backend/Application/Abstractions/Notifications/INotificationService.cs</c>. Two methods, and the
/// house rule that every contract returning one thing also returns many is satisfied by the pair.</para>
/// </summary>
public interface INotificationService
{
    /// <param name="dedupeKey">
    /// Logical idempotency key (e.g. <c>"task-{id}-due--1d"</c>). The implementation stores it
    /// <b>per recipient</b> (<c>"{key}:{userId}"</c>) so one logical reminder can fan out to several
    /// users while each user still gets it at most once. If the key already exists for that recipient
    /// the call silently skips.
    ///
    /// <para>This per-recipient scoping is load-bearing for the whole design: it is what makes one
    /// consolidated sweep (<c>PeriodicJobWorker</c>, replacing the backend's nine loops) safe to
    /// re-run, and therefore what lets a job be re-executed after a restart, a retry or an overlapping
    /// tick without duplicating a reminder. A null key means "always deliver".</para>
    /// </param>
    /// <param name="projectId">
    /// The entity's parent project, for entities whose screen is nested under one (ProjectTask,
    /// ProjectStage). Returned on the read and the real-time push so the client can build the route
    /// without a lookup. Optional and not part of dedupe.
    /// </param>
    Task NotifyAsync(
        string recipientUserId,
        NotificationType type,
        string title,
        string? body = null,
        NotificationEntityType? entityType = null,
        Guid? entityId = null,
        string? dedupeKey = null,
        Guid? projectId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Fan-out sibling of <see cref="NotifyAsync"/>. Empty and duplicate recipient ids are dropped;
    /// <paramref name="dedupeKey"/> carries the same per-recipient semantics, so N recipients produce
    /// at most N rows however many times this is called.
    /// </summary>
    Task NotifyManyAsync(
        IReadOnlyCollection<string> recipientUserIds,
        NotificationType type,
        string title,
        string? body = null,
        NotificationEntityType? entityType = null,
        Guid? entityId = null,
        string? dedupeKey = null,
        Guid? projectId = null,
        CancellationToken ct = default);
}
