using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Sites.Contracts;
using Sites.Contracts.Definition;
using Sites.Domain;
using Sites.Infrastructure.Persistence;

namespace Sites.Application.Features.Definition;

internal sealed class GetSiteModulesQueryHandler(SitesDbContext db)
    : IQueryHandler<GetSiteModulesQuery, IReadOnlyList<SiteModuleResponse>>
{
    public async Task<Result<IReadOnlyList<SiteModuleResponse>>> HandleAsync(
        GetSiteModulesQuery query, CancellationToken ct = default)
    {
        var modules = await db.SiteModules.AsNoTracking()
            .Where(m => query.IncludeInactive || m.IsActive)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Code)
            .Select(m => new SiteModuleResponse(
                m.Id, m.Code, m.NameEn, m.NameAr, m.DescriptionEn, m.DescriptionAr,
                m.IsSystem, m.SortOrder, m.IsActive))
            .ToListAsync(ct);

        return modules;
    }
}

/// <summary>
/// The surveyor's form, and the <b>only</b> consumer of <see cref="ISiteSchemaCache"/>. Everything else
/// that reads the definition — the validator, the pivot — queries the tables directly and is therefore
/// never stale. That asymmetry is deliberate: the cache serves the RENDERED form, while the rule that
/// judges a submission must be current.
/// </summary>
internal sealed class GetSiteModuleSchemaQueryHandler(SitesDbContext db, ISiteSchemaCache cache)
    : IQueryHandler<GetSiteModuleSchemaQuery, SiteModuleSchemaResponse>
{
    public async Task<Result<SiteModuleSchemaResponse>> HandleAsync(
        GetSiteModuleSchemaQuery query, CancellationToken ct = default)
    {
        var schema = await cache.GetOrAddAsync(query.Code, token => BuildAsync(query.Code, token), ct);

        return schema is null ? SiteErrors.ModuleNotFound : schema;
    }

    private async Task<SiteModuleSchemaResponse?> BuildAsync(string code, CancellationToken ct)
    {
        var normalized = code.Trim().ToLower();

        var module = await db.SiteModules.AsNoTracking()
            .Where(m => m.Code.ToLower() == normalized && m.IsActive)
            .Select(m => new
            {
                m.Id,
                m.Code,
                m.NameEn,
                m.NameAr,
                m.DescriptionEn,
                m.DescriptionAr,
                m.SortOrder,
            })
            .FirstOrDefaultAsync(ct);

        if (module is null) return null;

        // Ordered by ix_site_module_items_module_sort -- no sort node.
        var fields = await (
            from link in db.SiteModuleItems.AsNoTracking()
            join item in db.SiteItems.AsNoTracking() on link.ItemId equals item.Id
            where link.ModuleId == module.Id && link.IsActive && item.IsActive
            orderby link.SortOrder
            select new
            {
                LinkId = link.Id,
                link.ItemId,
                link.IsRequired,
                link.IsVisible,
                link.SortOrder,
                item.Code,
                item.NameEn,
                item.NameAr,
                item.DataType,
                item.Unit,
                item.MinValue,
                item.MaxValue,
                item.Tolerance,
                item.MaxLength,
                item.RegexPattern,
            }).ToListAsync(ct);

        // Options for the Select fields only, in one round trip rather than one per field.
        var selectItemIds = fields
            .Where(f => f.DataType == Sites.Domain.SiteItemDataType.Select)
            .Select(f => f.ItemId)
            .ToList();

        var options = selectItemIds.Count == 0
            ? []
            : await db.SiteItemOptions.AsNoTracking()
                .Where(o => selectItemIds.Contains(o.ItemId) && o.IsActive)
                .OrderBy(o => o.SortOrder)
                .Select(o => new
                {
                    o.ItemId,
                    Option = new SiteItemOptionResponse(o.Id, o.Code, o.NameEn, o.NameAr, o.SortOrder, o.IsActive),
                })
                .ToListAsync(ct);

        var optionsByItem = options
            .GroupBy(o => o.ItemId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<SiteItemOptionResponse>)[.. g.Select(o => o.Option)]);

        return new SiteModuleSchemaResponse(
            module.Id, module.Code, module.NameEn, module.NameAr,
            module.DescriptionEn, module.DescriptionAr, module.SortOrder,
            [.. fields.Select(f => new SiteSchemaFieldResponse(
                f.LinkId, f.ItemId, f.Code, f.NameEn, f.NameAr,
                SiteEnumMapping.ToContracts(f.DataType), f.Unit,
                f.MinValue, f.MaxValue, f.Tolerance, f.MaxLength, f.RegexPattern,
                f.IsRequired, f.IsVisible, f.SortOrder,
                optionsByItem.TryGetValue(f.ItemId, out var opts) ? opts : []))]);
    }
}

internal sealed class GetSiteItemsQueryHandler(SitesDbContext db)
    : IQueryHandler<GetSiteItemsQuery, IReadOnlyList<SiteItemResponse>>
{
    public async Task<Result<IReadOnlyList<SiteItemResponse>>> HandleAsync(
        GetSiteItemsQuery query, CancellationToken ct = default)
    {
        var items = await db.SiteItems.AsNoTracking()
            .Where(i => query.IncludeInactive || i.IsActive)
            .OrderBy(i => i.Code)
            .Select(i => new
            {
                i.Id,
                i.Code,
                i.NameEn,
                i.NameAr,
                i.DataType,
                i.Unit,
                i.MinValue,
                i.MaxValue,
                i.Tolerance,
                i.MaxLength,
                i.RegexPattern,
                i.IsSystem,
                i.IsActive,
            })
            .ToListAsync(ct);

        var selectItemIds = items
            .Where(i => i.DataType == Sites.Domain.SiteItemDataType.Select)
            .Select(i => i.Id)
            .ToList();

        // One round trip for every Select item's options, never one per item.
        var options = selectItemIds.Count == 0
            ? []
            : await db.SiteItemOptions.AsNoTracking()
                .Where(o => selectItemIds.Contains(o.ItemId))
                .OrderBy(o => o.SortOrder)
                .Select(o => new
                {
                    o.ItemId,
                    Option = new SiteItemOptionResponse(o.Id, o.Code, o.NameEn, o.NameAr, o.SortOrder, o.IsActive),
                })
                .ToListAsync(ct);

        var optionsByItem = options
            .GroupBy(o => o.ItemId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<SiteItemOptionResponse>)[.. g.Select(o => o.Option)]);

        IReadOnlyList<SiteItemResponse> responses =
        [
            .. items.Select(i => new SiteItemResponse(
                i.Id, i.Code, i.NameEn, i.NameAr, SiteEnumMapping.ToContracts(i.DataType), i.Unit,
                i.MinValue, i.MaxValue, i.Tolerance, i.MaxLength, i.RegexPattern,
                i.IsSystem, i.IsActive,
                optionsByItem.TryGetValue(i.Id, out var opts) ? opts : [])),
        ];

        return Result.Success(responses);
    }
}

/// <summary>
/// "Can I safely change this?" — the read behind the governance rules.
///
/// <para>The captured-value count deliberately does NOT join to <c>site_records</c>, so values on
/// soft-deleted records still count. That is consistent with the probe that gates the actual 409
/// (<c>ChangeSiteItemShapeCommandHandler</c>), which does the same — but it means an item can be
/// permanently frozen by data no longer reachable through the API. Ported as-is and recorded as an open
/// question (defect B17) rather than silently changed: deciding that deleted records release their hold
/// on an item's meaning is a product call, not a port.</para>
/// </summary>
internal sealed class GetSiteItemUsageQueryHandler(SitesDbContext db)
    : IQueryHandler<GetSiteItemUsageQuery, SiteItemUsageResponse>
{
    public async Task<Result<SiteItemUsageResponse>> HandleAsync(
        GetSiteItemUsageQuery query, CancellationToken ct = default)
    {
        var item = await db.SiteItems.AsNoTracking()
            .Where(i => i.Id == query.ItemId)
            .Select(i => new { i.Id, i.Code })
            .FirstOrDefaultAsync(ct);

        if (item is null) return SiteErrors.ItemNotFound;

        var capturedValueCount = await db.SiteRecordValues
            .CountAsync(v => v.ItemId == query.ItemId, ct);

        // No link.IsActive filter: a detached link still says where this item HAS been used, and the
        // flag is surfaced per row so the caller can tell the two apart.
        var modules = await (
            from link in db.SiteModuleItems.AsNoTracking()
            join module in db.SiteModules.AsNoTracking() on link.ModuleId equals module.Id
            where link.ItemId == query.ItemId
            orderby module.SortOrder, module.Code
            select new SiteItemUsageModuleResponse(module.Id, module.Code, link.IsRequired, link.IsActive))
            .ToListAsync(ct);

        return new SiteItemUsageResponse(
            item.Id, item.Code, capturedValueCount > 0, capturedValueCount, modules);
    }
}
