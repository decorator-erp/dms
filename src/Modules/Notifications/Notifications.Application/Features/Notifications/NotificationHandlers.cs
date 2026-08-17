using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Microsoft.EntityFrameworkCore;
using Notifications.Contracts;
using Notifications.Domain;
using Notifications.Domain.Errors;
using Notifications.Infrastructure.Persistence;
using SharedKernel;
using ContractsEntityType = Notifications.Contracts.NotificationEntityType;
using ContractsType = Notifications.Contracts.NotificationType;

namespace Notifications.Application.Features.Notifications;

/// <summary>
/// Every query below filters on <c>RecipientUserId</c> in the WHERE clause, not after materializing.
/// The endpoints fill that value from <c>ICurrentUser</c>, so "your inbox" is enforced by the query
/// rather than by the caller behaving.
/// </summary>
internal sealed class GetMyNotificationsQueryHandler(NotificationsDbContext db)
    : IQueryHandler<GetMyNotificationsQuery, PagedResult<NotificationResponse>>
{
    public async Task<Result<PagedResult<NotificationResponse>>> HandleAsync(
        GetMyNotificationsQuery query, CancellationToken ct = default)
    {
        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var rows = db.Notifications
            .AsNoTracking()
            .Where(n => n.RecipientUserId == query.RecipientUserId && n.IsActive);

        if (query.UnreadOnly)
            rows = rows.Where(n => n.ReadAtUtc == null);

        var total = await rows.CountAsync(ct);

        var items = await rows
            .OrderByDescending(n => n.CreatedAtUtc)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new NotificationResponse(
                n.Id,
                (ContractsType)(int)n.Type,
                n.Title,
                n.Body,
                n.EntityType == null ? null : (ContractsEntityType)(int)n.EntityType,
                n.EntityId,
                n.ReadAtUtc != null,
                n.ReadAtUtc,
                n.CreatedAtUtc))
            .ToListAsync(ct);

        return new PagedResult<NotificationResponse>(items, pageNumber, pageSize, total);
    }
}

internal sealed class GetUnreadNotificationCountQueryHandler(NotificationsDbContext db)
    : IQueryHandler<GetUnreadNotificationCountQuery, int>
{
    public async Task<Result<int>> HandleAsync(GetUnreadNotificationCountQuery query, CancellationToken ct = default) =>
        await db.Notifications.CountAsync(
            n => n.RecipientUserId == query.RecipientUserId && n.ReadAtUtc == null && n.IsActive, ct);
}

/// <summary>
/// Scoped by recipient in the lookup itself, so a caller cannot mark someone else's notification read
/// by guessing an id — and a miss returns <c>NotFound</c> whether the row belongs to another user or
/// does not exist, deliberately. Distinguishing the two would let any authenticated caller enumerate
/// other people's notification ids by probing status codes.
/// </summary>
internal sealed class MarkNotificationReadCommandHandler(NotificationsDbContext db, TimeProvider timeProvider)
    : ICommandHandler<MarkNotificationReadCommand>
{
    public async Task<Result> HandleAsync(MarkNotificationReadCommand command, CancellationToken ct = default)
    {
        var notification = await db.Notifications.FirstOrDefaultAsync(
            n => n.Id == command.NotificationId && n.RecipientUserId == command.RecipientUserId, ct);

        if (notification is null)
            return NotificationErrors.NotFound;

        notification.MarkRead(timeProvider.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}

internal sealed class MarkAllNotificationsReadCommandHandler(NotificationsDbContext db, TimeProvider timeProvider)
    : ICommandHandler<MarkAllNotificationsReadCommand>
{
    public async Task<Result> HandleAsync(MarkAllNotificationsReadCommand command, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var unread = await db.Notifications
            .Where(n => n.RecipientUserId == command.RecipientUserId && n.ReadAtUtc == null && n.IsActive)
            .ToListAsync(ct);

        foreach (var notification in unread)
            notification.MarkRead(now);

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
