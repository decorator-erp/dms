using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;
using Sites.Contracts.Surveys;
using Sites.Domain;
using Sites.Infrastructure.Persistence;

namespace Sites.Application.Features.Surveys;

/// <summary>
/// S8: <c>GetProjectsQueryValidator</c>'s paging bounds. Clamping answered a page the caller did not ask
/// for, and an unbounded page index overflowed <c>(page - 1) * size</c> into a negative OFFSET (500).
/// </summary>
internal sealed class GetSitesQueryValidator : AbstractValidator<GetSitesQuery>
{
    public GetSitesQueryValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.PageIndex).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Status).IsInEnum();
    }
}

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
        Result scope = await access.EnsureProjectAccessAsync(query.ProjectId, query.AppUserId, ProjectAccessArea.Sites, null, ct);
        if (scope.IsFailure)
        {
            return scope.Error;
        }

        SiteStatus? status = query.Status is { } s ? SiteEnumMapping.ToDomain(s) : (Domain.SiteStatus?)null;

        IQueryable<Site> filtered = db.Sites.AsNoTracking()
            .Where(x => x.ProjectId == query.ProjectId)
            .Where(x => status == null || x.Status == status);

        int total = await filtered.CountAsync(ct);

        // Bounded by GetSitesQueryValidator before this runs (S8), so the offset below cannot overflow.
        int page = query.PageIndex;
        int size = query.PageSize;

        // Ordered by creation, projected to exactly the columns ix_sites_project_status INCLUDEs -- the
        // covering set is why this is an index-only scan. Adding a field here means revisiting that index.
        List<SiteListItemResponse> items = await filtered
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(x => new SiteListItemResponse(
                x.Id, x.Code, SiteEnumMapping.ToContracts(x.Status), x.Address, x.SurveyedOn, x.CreatedAtUtc))
            .ToListAsync(ct);

        return new PagedResult<SiteListItemResponse>(items, page, size, total);
    }
}
