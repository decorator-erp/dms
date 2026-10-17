using BuildingBlocks;
using BuildingBlocks.Auditing;
using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Notifications.Contracts;
using SharedKernel;

namespace Notifications.Infrastructure.Endpoints;

/// <summary>
/// <b>The phase's only real authorization surface, and the split is structural rather than
/// conditional.</b>
///
/// <para>Three routes take <c>audit.read</c> and read anyone's rows. One route takes
/// <c>audit.view_own</c> and reads exactly the caller's, with the user id taken from
/// <see cref="ICurrentUser"/> — not from a route value, not from a query string, not from a body. A
/// <c>view_own</c> holder cannot reach the other three at all; they are not "the same route with a
/// filter applied", because a route that is one missing filter away from returning everyone is a
/// route that eventually returns everyone.</para>
///
/// <para>That is also why <c>/my-activity</c> uses its own query type
/// (<see cref="GetMyAuditActivityQuery"/>) rather than <see cref="GetAuditLogsQuery"/> with
/// <c>UserId</c> pre-filled: the self-scoped path has no parameter that could widen it, so widening it
/// would take a deliberate edit rather than an oversight.</para>
///
/// <para>Read-only by construction. Nothing in this module writes an audit row —
/// <c>AuditSaveChangesInterceptor</c> is the only writer, and its factory is <c>internal</c> to
/// BuildingBlocks so it cannot acquire a second one.</para>
/// </summary>
internal sealed class AuditEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/audit")
            .WithTags("Audit")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // ---- audit.read: the whole trail, anyone's rows. ----

        group.MapGet("/", async (
            IQueryHandler<GetAuditLogsQuery, PagedResult<AuditLogResponse>> handler,
            CancellationToken ct,
            string? userId = null,
            string? module = null,
            AuditAction? action = null,
            string? entityName = null,
            string? entityId = null,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            int pageNumber = 1,
            int pageSize = 25) =>
        {
            Result<PagedResult<AuditLogResponse>> result = await handler.HandleAsync(
                new GetAuditLogsQuery(userId, module, action, entityName, entityId, from, to, pageNumber, pageSize),
                ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        })
        .RequirePermission(NotificationsPermissions.AuditRead)
        .Produces<PagedResult<AuditLogResponse>>();

        group.MapGet("/entity/{entityName}/{entityId}", async (
            string entityName,
            string entityId,
            IQueryHandler<GetEntityAuditHistoryQuery, PagedResult<AuditLogResponse>> handler,
            CancellationToken ct,
            int pageNumber = 1,
            int pageSize = 25) =>
        {
            Result<PagedResult<AuditLogResponse>> result = await handler.HandleAsync(
                new GetEntityAuditHistoryQuery(entityName, entityId, pageNumber, pageSize), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        })
        .RequirePermission(NotificationsPermissions.AuditRead)
        .Produces<PagedResult<AuditLogResponse>>();

        // A named user's trail. Deliberately NOT reachable with audit.view_own: the whole point of the
        // split is that a self-scoped holder has no route taking someone else's id.
        group.MapGet("/user/{userId}", async (
            string userId,
            IQueryHandler<GetAuditLogsQuery, PagedResult<AuditLogResponse>> handler,
            CancellationToken ct,
            int pageNumber = 1,
            int pageSize = 25) =>
        {
            Result<PagedResult<AuditLogResponse>> result = await handler.HandleAsync(
                new GetAuditLogsQuery(userId, null, null, null, null, null, null, pageNumber, pageSize), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        })
        .RequirePermission(NotificationsPermissions.AuditRead)
        .Produces<PagedResult<AuditLogResponse>>();

        // ---- audit.view_own: exactly the caller's own rows, and no way to name another user. ----

        group.MapGet("/my-activity", async (
            ICurrentUser currentUser,
            IQueryHandler<GetMyAuditActivityQuery, PagedResult<AuditLogResponse>> handler,
            CancellationToken ct,
            int pageNumber = 1,
            int pageSize = 25) =>
        {
            if (currentUser.UserId is not { Length: > 0 } userId)
            {
                return Results.Unauthorized();
            }

            Result<PagedResult<AuditLogResponse>> result = await handler.HandleAsync(new GetMyAuditActivityQuery(userId, pageNumber, pageSize), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        })
        .RequirePermission(NotificationsPermissions.AuditViewOwn)
        .Produces<PagedResult<AuditLogResponse>>();
    }
}

/// <summary>The per-entity activity timeline — the human-readable feed, gated separately from the
/// field-level trail because they answer different questions for different audiences.</summary>
internal sealed class ActivityEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) => app.MapGet("/api/activities", async (
                                                                   IQueryHandler<GetActivitiesQuery, PagedResult<ActivityResponse>> handler,
                                                                   CancellationToken ct,
                                                                   string? entityName = null,
                                                                   string? entityId = null,
                                                                   Guid? projectId = null,
                                                                   int pageNumber = 1,
                                                                   int pageSize = 25) =>
                                                               {
                                                                   Result<PagedResult<ActivityResponse>> result = await handler.HandleAsync(
                                                                       new GetActivitiesQuery(entityName, entityId, projectId, pageNumber, pageSize), ct);
                                                                   return result.Match(Results.Ok, r => r.ToProblem());
                                                               })
        .WithTags("Activities")
        .RequirePermission(NotificationsPermissions.ActivitiesRead)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .Produces<PagedResult<ActivityResponse>>();
}
