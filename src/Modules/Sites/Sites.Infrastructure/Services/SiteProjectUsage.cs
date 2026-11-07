using Microsoft.EntityFrameworkCore;
using Sites.Contracts;
using Sites.Infrastructure.Persistence;

namespace Sites.Infrastructure.Services;

/// <summary><see cref="ISiteProjectUsage"/>: one query over the whole id batch, with the soft-delete query
/// filter switched off (a deactivated site still holds its project's foreign key).</summary>
internal sealed class SiteProjectUsage(SitesDbContext db) : ISiteProjectUsage
{
    public async Task<bool> IsProjectInUseAsync(Guid projectId, CancellationToken ct) =>
        (await ProjectsInUseAsync([projectId], ct)).Count > 0;

    public async Task<IReadOnlyList<Guid>> ProjectsInUseAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken ct)
    {
        if (projectIds.Count == 0)
        {
            return [];
        }

        return await db.Sites.AsNoTracking().IgnoreQueryFilters()
            .Where(s => projectIds.Contains(s.ProjectId))
            .Select(s => s.ProjectId)
            .Distinct()
            .ToListAsync(ct);
    }
}
