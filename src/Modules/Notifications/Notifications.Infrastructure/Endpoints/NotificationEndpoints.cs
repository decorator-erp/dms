using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Notifications.Contracts;

namespace Notifications.Infrastructure.Endpoints;

/// <summary>
/// The caller's own inbox. Every route resolves the recipient from <see cref="ICurrentUser"/> at the
/// endpoint and passes it into the command or query — never from a route value, a query string or a
/// body. <c>notifications.view_own</c> therefore gates the surface while the handler's WHERE clause
/// gates the data, and neither depends on the other being right.
/// </summary>
internal sealed class NotificationEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications")
            .WithTags("Notifications")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/", async (
            ICurrentUser currentUser,
            IQueryHandler<GetMyNotificationsQuery, PagedResult<NotificationResponse>> handler,
            CancellationToken ct,
            bool unreadOnly = false,
            int pageNumber = 1,
            int pageSize = 20) =>
        {
            if (currentUser.UserId is not { Length: > 0 } userId)
                return Results.Unauthorized();

            var result = await handler.HandleAsync(
                new GetMyNotificationsQuery(userId, unreadOnly, pageNumber, pageSize), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        })
        .RequirePermission(NotificationsPermissions.ViewOwn)
        .Produces<PagedResult<NotificationResponse>>();

        group.MapGet("/unread-count", async (
            ICurrentUser currentUser,
            IQueryHandler<GetUnreadNotificationCountQuery, int> handler,
            CancellationToken ct) =>
        {
            if (currentUser.UserId is not { Length: > 0 } userId)
                return Results.Unauthorized();

            var result = await handler.HandleAsync(new GetUnreadNotificationCountQuery(userId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        })
        .RequirePermission(NotificationsPermissions.ViewOwn)
        .Produces<int>();

        group.MapPut("/{notificationId:guid}/read", async (
            Guid notificationId,
            ICurrentUser currentUser,
            ICommandHandler<MarkNotificationReadCommand> handler,
            CancellationToken ct) =>
        {
            if (currentUser.UserId is not { Length: > 0 } userId)
                return Results.Unauthorized();

            var result = await handler.HandleAsync(new MarkNotificationReadCommand(notificationId, userId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        })
        .RequirePermission(NotificationsPermissions.ViewOwn)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status204NoContent);

        group.MapPut("/read-all", async (
            ICurrentUser currentUser,
            ICommandHandler<MarkAllNotificationsReadCommand> handler,
            CancellationToken ct) =>
        {
            if (currentUser.UserId is not { Length: > 0 } userId)
                return Results.Unauthorized();

            var result = await handler.HandleAsync(new MarkAllNotificationsReadCommand(userId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        })
        .RequirePermission(NotificationsPermissions.ViewOwn)
        .Produces(StatusCodes.Status204NoContent);
    }
}
