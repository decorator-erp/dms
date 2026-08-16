using Crm.Contracts;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crm.Infrastructure.Services;

/// <summary><see cref="ICrmSpaceReader"/>'s implementation — projects straight to the Contracts DTO
/// via <c>Select</c>, never load-then-map, matching <see cref="ClientDirectory"/>'s precedent. Added
/// at S1-01; first and only caller is <c>Sites.Application</c>, which validates
/// <c>SiteRecord.SpaceId</c> against the site's own project. Deliberately does NOT filter on
/// <c>IsActive</c> — the flag is projected instead, so the caller can distinguish "no such space" from
/// "that space is retired" and return the right error for each.</summary>
internal sealed class CrmSpaceReader(CrmDbContext db) : ICrmSpaceReader
{
    public Task<CrmSpaceSummary?> GetAsync(Guid spaceId, CancellationToken ct = default) =>
        db.CrmSpaces.AsNoTracking()
            .Where(s => s.Id == spaceId)
            .Select(s => new CrmSpaceSummary(s.Id, s.ProjectId, s.SpaceTypeId, s.Name, s.IsActive))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<CrmSpaceSummary>> GetManyAsync(
        IReadOnlyCollection<Guid> spaceIds, CancellationToken ct = default)
    {
        if (spaceIds.Count == 0) return [];

        return await db.CrmSpaces.AsNoTracking()
            .Where(s => spaceIds.Contains(s.Id))
            .Select(s => new CrmSpaceSummary(s.Id, s.ProjectId, s.SpaceTypeId, s.Name, s.IsActive))
            .ToListAsync(ct);
    }
}
