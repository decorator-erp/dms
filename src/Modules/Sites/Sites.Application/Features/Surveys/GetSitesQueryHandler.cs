using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;
using Sites.Contracts.Surveys;
using Sites.Infrastructure.Persistence;

namespace Sites.Application.Features.Surveys;

/// <summary>
/// Not <c>ISiteScoped</c> — it lists sites rather than reading one, so there is no site id to scope by.
/// Authorizes itself against the project, exactly as <c>CreateSiteCommandHandler</c> does.
/// </summary>
internal sealed class GetSitesQueryHandler(SitesDbContext db, IProjectAccessGuard access)
    : IQueryHandler<GetSitesQuery, PagedResult<SiteListItemResponse>>
{
    public async Task<Result<PagedResult<SiteListItemResponse>>> HandleAsync(
        GetSitesQuery query, CancellationToken ct = default)
    {
        var scope = await access.EnsureProjectAccessAsync(query.ProjectId, query.AppUserId, ct);
        if (scope.IsFailure) return scope.Error;

        var status = query.Status is { } s ? SiteEnumMapping.ToDomain(s) : (Domain.SiteStatus?)null;

        var filtered = db.Sites.AsNoTracking()
            .Where(x => x.ProjectId == query.ProjectId)
            .Where(x => status == null || x.Status == status);

        var total = await filtered.CountAsync(ct);

        // Clamped rather than trusted: an unbounded page size is a denial-of-service on a large project,
        // and a zero or negative page is a caller error that should still answer.
        var page = Math.Max(1, query.PageIndex);
        var size = Math.Clamp(query.PageSize, 1, 200);

        // Ordered by creation, projected to exactly the columns ix_sites_project_status INCLUDEs -- the
        // covering set is why this is an index-only scan. Adding a field here means revisiting that index.
        var items = await filtered
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(x => new SiteListItemResponse(
                x.Id, x.Code, SiteEnumMapping.ToContracts(x.Status), x.Address, x.SurveyedOn, x.CreatedAtUtc))
            .ToListAsync(ct);

        return new PagedResult<SiteListItemResponse>(items, page, size, total);
    }
}
