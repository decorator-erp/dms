using Microsoft.EntityFrameworkCore;
using Sites.Contracts;
using Sites.Infrastructure.Persistence;

namespace Sites.Infrastructure.Services;

/// <summary><see cref="ISiteDocumentReader"/>: three statements for any batch — the links, the records a
/// record-level link names, and the surveys — each under the module's soft-delete query filters.</summary>
internal sealed class SiteDocumentReader(SitesDbContext db) : ISiteDocumentReader
{
    public async Task<SiteDocumentSummary?> GetAsync(Guid siteDocumentId, CancellationToken ct) =>
        await GetManyAsync([siteDocumentId], ct) is [var first, ..] ? first : null;

    public async Task<IReadOnlyList<SiteDocumentSummary>> GetManyAsync(IReadOnlyCollection<Guid> siteDocumentIds, CancellationToken ct)
    {
        if (siteDocumentIds.Count == 0)
        {
            return [];
        }

        var links = await db.SiteDocuments.AsNoTracking()
            .Where(d => siteDocumentIds.Contains(d.Id))
            .Select(d => new { d.Id, d.SiteId, d.RecordId, d.DocumentId, d.Kind })
            .ToListAsync(ct);
        if (links.Count == 0)
        {
            return [];
        }

        List<Guid> recordIds = [.. links.Where(l => l.RecordId is not null).Select(l => l.RecordId!.Value).Distinct()];
        Dictionary<Guid, Guid> recordSites = recordIds.Count == 0
            ? []
            : await db.SiteRecords.AsNoTracking()
                .Where(r => recordIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.SiteId, ct);

        Guid? SiteOf(Guid? siteId, Guid? recordId) =>
            siteId ?? (recordId is { } r && recordSites.TryGetValue(r, out Guid s) ? s : null);

        List<Guid> siteIds = [.. links.Select(l => SiteOf(l.SiteId, l.RecordId)).OfType<Guid>().Distinct()];
        var sites = await db.Sites.AsNoTracking()
            .Where(s => siteIds.Contains(s.Id))
            .Select(s => new { s.Id, s.ProjectId, s.Code })
            .ToDictionaryAsync(s => s.Id, ct);

        List<SiteDocumentSummary> result = [];
        foreach (var link in links)
        {
            if (SiteOf(link.SiteId, link.RecordId) is { } siteId && sites.TryGetValue(siteId, out var site))
            {
                result.Add(new SiteDocumentSummary(
                    link.Id, siteId, link.RecordId, site.ProjectId, link.DocumentId, (SiteDocumentKind)link.Kind, site.Code));
            }
        }

        return result;
    }
}
