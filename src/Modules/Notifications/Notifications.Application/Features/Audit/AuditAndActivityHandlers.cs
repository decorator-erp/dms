using BuildingBlocks.Auditing;
using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Microsoft.EntityFrameworkCore;
using Notifications.Contracts;
using Notifications.Infrastructure.Persistence;
using SharedKernel;

namespace Notifications.Application.Features.Audit;

/// <summary>
/// Audit reads. <b>Nothing here writes an <see cref="AuditLog"/></b> — <c>AuditSaveChangesInterceptor</c>
/// in BuildingBlocks is the only writer, and its factory is <c>internal</c> to that assembly precisely
/// so this module cannot become a second one.
///
/// <para>The rows live in <c>shared.audit_logs</c>, mapped onto <c>NotificationsDbContext</c> with
/// <c>ExcludeFromMigrations</c>. Reading a <c>shared</c> table through a module context is the
/// established shape, not a compromise — every module already maps the outbox tables the same way.</para>
/// </summary>
internal static class AuditQueryProjection
{
    internal static IQueryable<AuditLogResponse> Project(IQueryable<AuditLog> rows) =>
        rows.Select(a => new AuditLogResponse(
            a.Id, a.UserId, a.UserEmail, a.IpAddress, a.Action, a.EntityName, a.EntityId,
            a.OldValues, a.NewValues, a.ChangedColumns, a.Module, a.Timestamp, a.RequestPath, a.RequestMethod));

    internal static async Task<PagedResult<AuditLogResponse>> PageAsync(
        IQueryable<AuditLog> rows, int pageNumber, int pageSize, CancellationToken ct)
    {
        var page = pageNumber < 1 ? 1 : pageNumber;
        var size = Math.Clamp(pageSize, 1, 100);

        var total = await rows.CountAsync(ct);
        var items = await Project(rows.OrderByDescending(a => a.Timestamp).Skip((page - 1) * size).Take(size))
            .ToListAsync(ct);

        return new PagedResult<AuditLogResponse>(items, page, size, total);
    }
}

internal sealed class GetAuditLogsQueryHandler(NotificationsDbContext db)
    : IQueryHandler<GetAuditLogsQuery, PagedResult<AuditLogResponse>>
{
    public async Task<Result<PagedResult<AuditLogResponse>>> HandleAsync(
        GetAuditLogsQuery query, CancellationToken ct = default)
    {
        var rows = db.Set<AuditLog>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.UserId))
            rows = rows.Where(a => a.UserId == query.UserId);
        if (!string.IsNullOrWhiteSpace(query.Module))
            rows = rows.Where(a => a.Module == query.Module);
        if (query.Action is { } action)
            rows = rows.Where(a => a.Action == action);
        if (!string.IsNullOrWhiteSpace(query.EntityName))
            rows = rows.Where(a => a.EntityName == query.EntityName);
        if (!string.IsNullOrWhiteSpace(query.EntityId))
            rows = rows.Where(a => a.EntityId == query.EntityId);
        if (query.From is { } from)
            rows = rows.Where(a => a.Timestamp >= from);
        if (query.To is { } to)
            rows = rows.Where(a => a.Timestamp <= to);

        return await AuditQueryProjection.PageAsync(rows, query.PageNumber, query.PageSize, ct);
    }
}

internal sealed class GetEntityAuditHistoryQueryHandler(NotificationsDbContext db)
    : IQueryHandler<GetEntityAuditHistoryQuery, PagedResult<AuditLogResponse>>
{
    public async Task<Result<PagedResult<AuditLogResponse>>> HandleAsync(
        GetEntityAuditHistoryQuery query, CancellationToken ct = default)
    {
        var rows = db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.EntityName == query.EntityName && a.EntityId == query.EntityId);

        return await AuditQueryProjection.PageAsync(rows, query.PageNumber, query.PageSize, ct);
    }
}

/// <summary>
/// <b>The self-scoped read, and the scoping is in the WHERE clause.</b> Its <c>UserId</c> comes from
/// <c>ICurrentUser</c> at the endpoint and from nowhere else — no route value, no query string. A
/// separate handler from <see cref="GetAuditLogsQueryHandler"/> rather than a flag on it, so the
/// self-scoped path has no parameter that could widen it.
/// </summary>
internal sealed class GetMyAuditActivityQueryHandler(NotificationsDbContext db)
    : IQueryHandler<GetMyAuditActivityQuery, PagedResult<AuditLogResponse>>
{
    public async Task<Result<PagedResult<AuditLogResponse>>> HandleAsync(
        GetMyAuditActivityQuery query, CancellationToken ct = default)
    {
        var rows = db.Set<AuditLog>().AsNoTracking().Where(a => a.UserId == query.UserId);

        return await AuditQueryProjection.PageAsync(rows, query.PageNumber, query.PageSize, ct);
    }
}

internal sealed class GetActivitiesQueryHandler(NotificationsDbContext db)
    : IQueryHandler<GetActivitiesQuery, PagedResult<ActivityResponse>>
{
    public async Task<Result<PagedResult<ActivityResponse>>> HandleAsync(
        GetActivitiesQuery query, CancellationToken ct = default)
    {
        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var rows = db.Activities.AsNoTracking().Where(a => a.IsActive);

        if (!string.IsNullOrWhiteSpace(query.EntityName))
            rows = rows.Where(a => a.EntityName == query.EntityName);
        if (!string.IsNullOrWhiteSpace(query.EntityId))
            rows = rows.Where(a => a.EntityId == query.EntityId);
        if (query.ProjectId is { } projectId)
            rows = rows.Where(a => a.ProjectId == projectId);

        var total = await rows.CountAsync(ct);

        var items = await rows
            .OrderByDescending(a => a.OccurredAtUtc)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new ActivityResponse(
                a.Id, a.ActorUserId, a.ActorName, (AuditAction)(int)a.Action, a.EntityName, a.EntityId,
                a.EntityDisplayName, a.ProjectId, a.Message, a.OccurredAtUtc))
            .ToListAsync(ct);

        return new PagedResult<ActivityResponse>(items, pageNumber, pageSize, total);
    }
}
