using BuildingBlocks.Auditing;
using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;

namespace Notifications.Contracts;

// ---------------------------------------------------------------------------------------------
// Notifications — the caller's own inbox. Every command and query carries the recipient/actor id
// explicitly rather than reading ICurrentUser inside the handler: the endpoint resolves the caller
// once and passes it in, the shape CRM adopted at G9-14 and Sites at S1-08. It also makes these
// handlers testable without an HttpContext, and makes "whose rows is this?" visible in the type.
// ---------------------------------------------------------------------------------------------

public sealed record NotificationResponse(
    Guid Id,
    NotificationType Type,
    string Title,
    string? Body,
    NotificationEntityType? EntityType,
    Guid? EntityId,
    bool IsRead,
    DateTime? ReadAtUtc,
    DateTime CreatedAtUtc,
    Guid? ProjectId);

public sealed record GetMyNotificationsQuery(string RecipientUserId, bool UnreadOnly, int PageNumber, int PageSize)
    : IQuery<PagedResult<NotificationResponse>>;

public sealed record GetUnreadNotificationCountQuery(string RecipientUserId) : IQuery<int>;

/// <summary>Scoped by <paramref name="RecipientUserId"/>, not merely filtered for display — a caller
/// must not be able to mark another user's notification read by guessing an id.</summary>
public sealed record MarkNotificationReadCommand(Guid NotificationId, string RecipientUserId) : ICommand;

public sealed record MarkAllNotificationsReadCommand(string RecipientUserId) : ICommand;

// ---------------------------------------------------------------------------------------------
// Activities — the human-readable per-entity timeline.
// ---------------------------------------------------------------------------------------------

public sealed record ActivityResponse(
    Guid Id,
    string? ActorUserId,
    string? ActorName,
    AuditAction Action,
    string EntityName,
    string? EntityId,
    string? EntityDisplayName,
    Guid? ProjectId,
    string Message,
    DateTime OccurredAtUtc);

public sealed record GetActivitiesQuery(
    string? EntityName, string? EntityId, Guid? ProjectId, int PageNumber, int PageSize)
    : IQuery<PagedResult<ActivityResponse>>;

// ---------------------------------------------------------------------------------------------
// Audit — the field-level change trail. Read-only; nothing in this module writes an AuditLog, the
// interceptor in BuildingBlocks does.
// ---------------------------------------------------------------------------------------------

public sealed record AuditLogResponse(
    Guid Id,
    string? UserId,
    string? UserEmail,
    string? IpAddress,
    AuditAction Action,
    string EntityName,
    string? EntityId,
    string? OldValues,
    string? NewValues,
    string? ChangedColumns,
    string? Module,
    DateTimeOffset Timestamp,
    string? RequestPath,
    string? RequestMethod);

/// <summary>The full log. Reachable only with <c>audit.read</c>.</summary>
public sealed record GetAuditLogsQuery(
    string? UserId, string? Module, AuditAction? Action, string? EntityName, string? EntityId,
    DateTimeOffset? From, DateTimeOffset? To, int PageNumber, int PageSize)
    : IQuery<PagedResult<AuditLogResponse>>;

/// <summary>Everything that happened to one row. Reachable only with <c>audit.read</c>.</summary>
public sealed record GetEntityAuditHistoryQuery(string EntityName, string EntityId, int PageNumber, int PageSize)
    : IQuery<PagedResult<AuditLogResponse>>;

/// <summary>
/// One user's own trail. <b>The <c>UserId</c> here is filled from <c>ICurrentUser</c> at the endpoint
/// and never from a route or query parameter</b> — that is the entire difference between
/// <c>audit.view_own</c> and <c>audit.read</c>, and it is the phase's only real authorization surface.
/// A separate query type rather than a flag on <see cref="GetAuditLogsQuery"/>, so the self-scoped
/// route cannot be widened by passing one more argument.
/// </summary>
public sealed record GetMyAuditActivityQuery(string UserId, int PageNumber, int PageSize)
    : IQuery<PagedResult<AuditLogResponse>>;
