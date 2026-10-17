using BuildingBlocks.Messaging;
using Crm.Contracts;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Sites.Contracts;
using Sites.Contracts.Definition;
using Sites.Contracts.Records;
using Sites.Domain;
using Sites.Infrastructure.Persistence;

namespace Sites.Application.Features.Records;

/// <summary>
/// The grid: one row per record, one cell per field of its module.
///
/// <para>Three backend defects fixed here — B9, B10 and B11 — all in the same handler.</para>
/// </summary>
internal sealed class GetSitePivotQueryHandler(SitesDbContext db, ICrmSpaceReader spaces)
    : IQueryHandler<GetSitePivotQuery, SitePivotResponse>
{
    public async Task<Result<SitePivotResponse>> HandleAsync(
        GetSitePivotQuery query, CancellationToken ct = default)
    {
        // Defect B10's fix: the backend omits the IsActive filter here while the schema route requires
        // it, so GET /records rendered a retired module's grid that GET /schema 404s for.
        var module = await db.SiteModules.AsNoTracking()
            .Where(m => m.Id == query.ModuleId && m.IsActive)
            .Select(m => new { m.Id, m.Code })
            .FirstOrDefaultAsync(ct);

        if (module is null)
        {
            return SiteErrors.ModuleNotFound;
        }

        var fields = await (
            from link in db.SiteModuleItems.AsNoTracking()
            join item in db.SiteItems.AsNoTracking() on link.ItemId equals item.Id
            where link.ModuleId == query.ModuleId && link.IsActive && item.IsActive
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

        // Defect B9's fix (first half): the backend builds every column with an EMPTY option list, so a
        // client rendering a Select column from the pivot alone has no choices to offer. One batched
        // query, only for the Select fields.
        var selectItemIds = fields
            .Where(f => f.DataType == Sites.Domain.SiteItemDataType.Select)
            .Select(f => f.ItemId)
            .ToList();

        Dictionary<Guid, IReadOnlyList<SiteItemOptionResponse>> optionsByItem = selectItemIds.Count == 0
            ? []
            : (await db.SiteItemOptions.AsNoTracking()
                    .Where(o => selectItemIds.Contains(o.ItemId) && o.IsActive)
                    .OrderBy(o => o.SortOrder)
                    .Select(o => new
                    {
                        o.ItemId,
                        Option = new SiteItemOptionResponse(
                            o.Id, o.Code, o.NameEn, o.NameAr, o.SortOrder, o.IsActive),
                    })
                    .ToListAsync(ct))
                .GroupBy(o => o.ItemId)
                .ToDictionary(
                    g => g.Key, g => (IReadOnlyList<SiteItemOptionResponse>)[.. g.Select(o => o.Option)]);

        // Includes IsVisible = false fields on purpose: the export carries them, the UI hides them.
        var columns = fields
            .Select(f => new SiteSchemaFieldResponse(
                f.LinkId, f.ItemId, f.Code, f.NameEn, f.NameAr,
                SiteEnumMapping.ToContracts(f.DataType), f.Unit, f.MinValue, f.MaxValue, f.Tolerance,
                f.MaxLength, f.RegexPattern, f.IsRequired, f.IsVisible, f.SortOrder,
                optionsByItem.TryGetValue(f.ItemId, out IReadOnlyList<SiteItemOptionResponse>? opts) ? opts : []))
            .ToList();

        var records = await db.SiteRecords.AsNoTracking()
            .Where(r => r.SiteId == query.SiteId && r.ModuleId == query.ModuleId)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Label)
            .Select(r => new { r.Id, r.Label, r.SpaceId, r.SortOrder, r.Note })
            .ToListAsync(ct);

        var recordIds = records.Select(r => r.Id).ToList();

        // Defect B11's fix: the backend resolves the option's code and both names with THREE separate
        // correlated subqueries per cell -- 1250 cells in a 50x25 grid meant 3750 scalar subqueries.
        // One left join instead.
        var cells = recordIds.Count == 0
            ? []
            : await (
                from value in db.SiteRecordValues.AsNoTracking()
                join item in db.SiteItems.AsNoTracking() on value.ItemId equals item.Id
                join option in db.SiteItemOptions.AsNoTracking() on value.OptionId equals option.Id into opts
                from option in opts.DefaultIfEmpty()
                where recordIds.Contains(value.RecordId)
                select new
                {
                    value.RecordId,
                    item.Code,
                    value.ValueNumber,
                    value.ValueText,
                    value.ValueBool,
                    value.ValueDate,
                    value.OptionId,
                    OptionCode = option != null ? option.Code : null,
                    OptionNameEn = option != null ? option.NameEn : null,
                    OptionNameAr = option != null ? option.NameAr : null,
                }).ToListAsync(ct);

        var cellsByRecord = cells.GroupBy(c => c.RecordId).ToDictionary(g => g.Key, g => g.ToList());

        // Space names come from CRM through its own contract, batched -- never a join into crm's tables.
        var spaceIds = records.Where(r => r.SpaceId.HasValue).Select(r => r.SpaceId!.Value).Distinct().ToList();
        Dictionary<Guid, string> spaceNames = spaceIds.Count == 0
            ? []
            : (await spaces.GetManyAsync(spaceIds, ct)).ToDictionary(s => s.Id, s => s.Name);

        var columnCodes = columns.Select(c => c.Code).ToHashSet();

        var rows = records.Select(r =>
        {
            var map = new Dictionary<string, SitePivotCellResponse?>();

            // Every column gets a key, present or not -- a record missing an optional value must still
            // render a blank cell rather than a ragged row.
            foreach (SiteSchemaFieldResponse? column in columns)
            {
                map[column.Code] = null;
            }

            if (cellsByRecord.TryGetValue(r.Id, out var found))
            {
                foreach (var cell in found)
                {
                    // Defect B9's fix (second half): the backend assigns through a Dictionary indexer,
                    // which ADDS missing keys -- so a value captured against an item since detached or
                    // deactivated (filtered out of `columns` above) appeared as an extra cell with no
                    // matching column, the ragged row the code one line up promises never to produce.
                    if (!columnCodes.Contains(cell.Code))
                    {
                        continue;
                    }

                    map[cell.Code] = new SitePivotCellResponse(
                        cell.ValueNumber, cell.ValueText, cell.ValueBool, cell.ValueDate,
                        cell.OptionId, cell.OptionCode, cell.OptionNameEn, cell.OptionNameAr);
                }
            }

            string? spaceName = r.SpaceId is { } id && spaceNames.TryGetValue(id, out string? name) ? name : null;

            return new SitePivotRowResponse(r.Id, r.Label, r.SpaceId, spaceName, r.SortOrder, r.Note, map);
        }).ToList();

        return new SitePivotResponse(query.SiteId, module.Id, module.Code, columns, rows);
    }
}

/// <summary>Records bucketed by room, with the whole-unit records last under a synthetic group.</summary>
internal sealed class GetSiteRecordsGroupedQueryHandler(SitesDbContext db, ICrmSpaceReader spaces)
    : IQueryHandler<GetSiteRecordsGroupedQuery, SiteGroupedResponse>
{
    private const string WholeUnit = "Whole unit / عام";

    public async Task<Result<SiteGroupedResponse>> HandleAsync(
        GetSiteRecordsGroupedQuery query, CancellationToken ct = default)
    {
        var records = await db.SiteRecords.AsNoTracking()
            .Where(r => r.SiteId == query.SiteId)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Label)
            .Select(r => new
            {
                r.Id,
                r.ModuleId,
                r.Label,
                r.SpaceId,
                r.CatalogItemId,
                r.SortOrder,
                r.Note,
            })
            .ToListAsync(ct);

        // The backend LEFT JOINs crm.crm_spaces directly. A module may not read another module's tables,
        // so the names come batched through ICrmSpaceReader instead -- one call, not one per record.
        var spaceIds = records.Where(r => r.SpaceId.HasValue).Select(r => r.SpaceId!.Value).Distinct().ToList();
        Dictionary<Guid, string> spaceById = spaceIds.Count == 0
            ? []
            : (await spaces.GetManyAsync(spaceIds, ct)).ToDictionary(s => s.Id, s => s.Name);

        var groups = records
            .GroupBy(r => r.SpaceId)
            // NULLs last: the main entrance door and the electrical panel belong to the unit, not a room.
            .Select(g => new
            {
                SpaceId = g.Key,
                Name = g.Key is { } id && spaceById.TryGetValue(id, out string? n) ? n : null,
                Records = g.ToList(),
            })
            .OrderBy(g => g.SpaceId is null ? 1 : 0)
            .ThenBy(g => g.Name, StringComparer.Ordinal)
            .Select(g => new SiteSpaceGroupResponse(
                g.SpaceId,
                g.Name ?? WholeUnit,
                [.. g.Records.Select(r => new SiteRecordResponse(
                    r.Id, r.ModuleId, r.Label, r.SpaceId, g.Name,
                    r.CatalogItemId, r.SortOrder, r.Note))]))
            .ToList();

        return new SiteGroupedResponse(query.SiteId, groups);
    }
}

/// <summary>
/// Families — distinct combinations of a caller-chosen key.
///
/// <para>The <c>groupBy</c> parameter is caller-supplied and reaches a hand-written SQL statement, so it
/// is worth being explicit about why that is safe: it is never sanitised or escaped, it is
/// <b>resolved</b>. Each token must match a field of this module by code; unmatched tokens are a 400
/// naming them, and what leaves this handler is a list of item ids read out of the database, bound as a
/// <c>uuid[]</c> parameter. The caller's bytes do not survive the lookup.</para>
/// </summary>
internal sealed class GetSiteFamiliesQueryHandler(SitesDbContext db, ISiteReadQueries readQueries)
    : IQueryHandler<GetSiteFamiliesQuery, SiteFamiliesResponse>
{
    private static readonly string[] Dimensional = ["width_cm", "height_cm", "length_cm", "mount_height_cm"];

    public async Task<Result<SiteFamiliesResponse>> HandleAsync(
        GetSiteFamiliesQuery query, CancellationToken ct = default)
    {
        Result<List<(Guid ItemId, string Code)>> resolved = await ResolveGroupByAsync(query.ModuleId, query.GroupBy, ct);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        List<(Guid ItemId, string Code)> keys = resolved.Value!;

        IReadOnlyList<SiteFamilyResponse> families = await readQueries.GetFamiliesAsync(
            query.SiteId, query.ModuleId, [.. keys.Select(k => k.ItemId)], ct);

        return new SiteFamiliesResponse(
            query.SiteId, query.ModuleId, [.. keys.Select(k => k.Code)], families);
    }

    /// <summary>
    /// The contract is closed: absent or <c>exact</c> means every required field of the module;
    /// <c>size</c> means its dimensional fields; anything else is a comma-separated list of item codes,
    /// each of which must resolve to an active field of THIS module. An unknown token is a 400 naming it,
    /// never a silent fallback — otherwise the parameter becomes "whatever string reaches the switch".
    /// Returns the canonical code and id from the database, never the caller's string.
    /// </summary>
    private async Task<Result<List<(Guid ItemId, string Code)>>> ResolveGroupByAsync(
        Guid moduleId, string? groupBy, CancellationToken ct)
    {
        var linked = await (
            from link in db.SiteModuleItems.AsNoTracking()
            join item in db.SiteItems.AsNoTracking() on link.ItemId equals item.Id
            where link.ModuleId == moduleId && link.IsActive && item.IsActive
            orderby link.SortOrder
            select new { item.Id, item.Code, link.IsRequired })
            .ToListAsync(ct);

        if (linked.Count == 0)
        {
            return SiteErrors.ModuleNotFound;
        }

        string? token = groupBy?.Trim();

        if (string.IsNullOrEmpty(token) || token.Equals("exact", StringComparison.OrdinalIgnoreCase))
        {
            var required = linked.Where(f => f.IsRequired).Select(f => (f.Id, f.Code)).ToList();
            return required.Count > 0 ? required : [.. linked.Select(f => (f.Id, f.Code))];
        }

        if (token.Equals("size", StringComparison.OrdinalIgnoreCase))
        {
            var sized = linked
                .Where(f => Dimensional.Contains(f.Code))
                .OrderBy(f => Array.IndexOf(Dimensional, f.Code))
                .Select(f => (f.Id, f.Code))
                .ToList();

            return sized.Count > 0
                ? sized
                : Error.BadRequest(
                    "sites.families.NoDimensions", "This module has no dimensional fields to group by.");
        }

        List<string> requested = token
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        // "," and ", ," split to nothing, so the loop below never ran, `unknown` stayed empty, and the
        // caller got 200 with no families -- a silent empty answer to a groupBy that named no field this
        // module has. An unresolvable groupBy is a 400, and naming no field at all is unresolvable.
        if (requested.Count == 0)
        {
            return Error.BadRequest(
                "sites.families.UnknownGroupBy",
                "groupBy named no fields. Supply field codes separated by commas, or omit it.");
        }

        var resolved = new List<(Guid, string)>(requested.Count);
        var unknown = new List<string>();

        foreach (string? code in requested)
        {
            // Match on the caller's token, but carry forward the row's own id and code.
            var match = linked.FirstOrDefault(f => f.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                unknown.Add(code);
            }

            // Defect B14: the backend appends one entry per requested token with no distinct check, so
            // ?groupBy=width_cm,width_cm produced a family key of "width_cm=90 | width_cm=90". FamilyKey
            // is documented as the exporter's join token, so a caller could mint keys matching nothing.
            else if (!resolved.Any(r => r.Item1 == match.Id))
            {
                resolved.Add((match.Id, match.Code));
            }
        }

        if (unknown.Count > 0)
        {
            return Error.BadRequest(
                "sites.families.UnknownGroupBy",
                $"Not a field of this module: {string.Join(", ", unknown)}.");
        }

        return resolved;
    }
}

/// <summary>The BOQ rollup, behind the same narrow read port as families.</summary>
internal sealed class GetSiteBoqQueryHandler(ISiteReadQueries readQueries)
    : IQueryHandler<GetSiteBoqQuery, SiteBoqResponse>
{
    public async Task<Result<SiteBoqResponse>> HandleAsync(
        GetSiteBoqQuery query, CancellationToken ct = default)
    {
        IReadOnlyList<SiteBoqLineResponse> lines = await readQueries.GetBoqAsync(query.SiteId, ct);
        return new SiteBoqResponse(query.SiteId, lines);
    }
}
