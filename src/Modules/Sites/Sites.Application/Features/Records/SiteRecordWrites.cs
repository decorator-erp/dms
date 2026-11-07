using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Sites.Application.Features.Surveys;
using Sites.Contracts.Records;
using Sites.Domain;
using Sites.Domain.Definition;
using Sites.Infrastructure.Persistence;

namespace Sites.Application.Features.Records;

/// <summary>Record-layer helpers shared by the single-record and grid-save paths.</summary>
internal static class SiteRecordWrites
{
    /// <summary>
    /// Loads a record for writing, having proved it belongs to this site AND that the site still accepts
    /// writes. Both halves matter: without the ownership check the route's site segment is decoration;
    /// without the editable check an approved survey's measurements could still move.
    /// </summary>
    public static async Task<Result<(Site Site, SiteRecord Record)>> LoadEditableAsync(
        SitesDbContext db, Guid siteId, Guid recordId, CancellationToken ct)
    {
        Site? site = await SiteWrites.LoadForWriteAsync(db, siteId, ct);
        if (site is null)
        {
            return SiteErrors.NotFound;
        }

        if (!site.IsEditable)
        {
            return SiteErrors.SiteLocked;
        }

        Result<SiteRecord> record = await SiteWrites.LoadRecordForWriteAsync(db, siteId, recordId, ct);
        if (record.IsFailure)
        {
            return record.Error;
        }

        return (site, record.Value!);
    }

    /// <summary>
    /// Case-insensitive label uniqueness within (site, module), among ACTIVE rows — mirroring
    /// <c>ux_site_records_label</c>, so the caller gets a 409 rather than a constraint violation.
    /// </summary>
    public static Task<Result> EnsureLabelFreeAsync(
        SitesDbContext db, Guid siteId, Guid moduleId, string label, Guid? excludingRecordId,
        CancellationToken ct) =>
        EnsureLabelFreeOfBatchAsync(
            db, siteId, moduleId, label, excludingRecordId is { } id ? [id] : [], ct);

    /// <summary>
    /// The grid save's form (S9): excludes EVERY stored record the batch rewrites, not just the row's
    /// own. Their labels are about to change, and the in-batch duplicate check already judges them
    /// against each other — so swapping two labels is not "taken".
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1304:Specify CultureInfo",
        Justification = "The column's ToLower() inside the query runs as SQL lower(); ToLowerInvariant and the culture overloads do not translate.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1311:Specify a culture or use an invariant version",
        Justification = "Same query: translated to SQL lower(), never run in .NET.")]
    public static async Task<Result> EnsureLabelFreeOfBatchAsync(
        SitesDbContext db, Guid siteId, Guid moduleId, string label,
        IReadOnlyCollection<Guid> excludingRecordIds, CancellationToken ct)
    {
        string normalized = label.Trim().ToLowerInvariant();

        bool taken = await db.SiteRecords
            .AnyAsync(
                r => r.SiteId == siteId
                     && r.ModuleId == moduleId
                     && r.Label.ToLower() == normalized
                     && !excludingRecordIds.Contains(r.Id),
                ct);

        return taken ? SiteErrors.LabelTaken : Result.Success();
    }

    /// <summary>
    /// The grid save's label probe (S7): every row's (module, label) pair judged in ONE query, against
    /// the site's active records outside the batch. Returns the pairs already taken, each label in its
    /// trimmed lower-case form — the same comparison <see cref="EnsureLabelFreeOfBatchAsync"/> makes.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1304:Specify CultureInfo",
        Justification = "The column's ToLower() inside the query runs as SQL lower(); ToLowerInvariant and the culture overloads do not translate.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1311:Specify a culture or use an invariant version",
        Justification = "Same query: translated to SQL lower(), never run in .NET.")]
    public static async Task<HashSet<(Guid ModuleId, string Label)>> TakenLabelsAsync(
        SitesDbContext db, Guid siteId, IReadOnlyCollection<(Guid ModuleId, string Label)> candidates,
        IReadOnlyCollection<Guid> excludingRecordIds, CancellationToken ct)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var moduleIds = candidates.Select(c => c.ModuleId).Distinct().ToList();
        var labels = candidates.Select(c => Normalize(c.Label)).Distinct().ToList();

        var stored = await db.SiteRecords
            .Where(r => r.SiteId == siteId
                        && moduleIds.Contains(r.ModuleId)
                        && labels.Contains(r.Label.ToLower())
                        && !excludingRecordIds.Contains(r.Id))
            .Select(r => new { r.ModuleId, Label = r.Label.ToLower() })
            .ToListAsync(ct);

        return stored.Select(r => (r.ModuleId, r.Label)).ToHashSet();
    }

    /// <summary>The label form every uniqueness check compares: trimmed, lower-case.</summary>
    public static string Normalize(string label) => label.Trim().ToLowerInvariant();

    /// <summary>Stages a record's whole value set, then saves.</summary>
    public static async Task<Result> ReplaceValuesAsync(
        SitesDbContext db, Site site, SiteRecord record, IReadOnlyList<SiteValueRequest> values,
        CancellationToken ct)
    {
        Result staged = await StageValuesAsync(db, record, values, ct);
        if (staged.IsFailure)
        {
            return staged;
        }

        // NS1: the record's xmin guards its value set, the site's guards the freeze.
        return await SiteWrites.SaveGuardedAsync(db, site, [record], ct);
    }

    /// <summary>
    /// Replaces a record's value set on the change tracker without saving, so the grid save can stage
    /// many records inside one transaction.
    ///
    /// <para>Delete-then-insert over the module's CURRENT items, because a value absent from the incoming
    /// set is <b>cleared</b>: the table has no soft-delete flag, and a cleared measurement is not a
    /// measurement. Values of items no longer on the form are left alone (B-S-H2).</para>
    ///
    /// <para>Item types and options are loaded in two batched queries, never per value — the N+1 the
    /// backend's own domain service carries a comment about having removed.</para>
    /// </summary>
    public static Task<Result> StageValuesAsync(
        SitesDbContext db, SiteRecord record, IReadOnlyList<SiteValueRequest> values, CancellationToken ct) =>
        StageValuesManyAsync(db, [(record, values)], ct);

    /// <summary>
    /// <see cref="StageValuesAsync"/> for a whole grid (S7): three queries whatever the row count — the
    /// modules' allowed items, the records' stored values, the supplied options — and the rest in memory.
    /// Stops at the first failing record, in the order given, with the error the single form returns.
    /// </summary>
    public static async Task<Result> StageValuesManyAsync(
        SitesDbContext db, IReadOnlyList<(SiteRecord Record, IReadOnlyList<SiteValueRequest> Values)> pending,
        CancellationToken ct)
    {
        if (pending.Count == 0)
        {
            return Result.Success();
        }

        var moduleIds = pending.Select(p => p.Record.ModuleId).Distinct().ToList();

        // Only items actually linked to a record's module may carry a value. The validator says the
        // same thing with a nicer message; this is the structural guard, and it is what stops a value
        // being written against a field the module does not have.
        var allowedByModule = (await (
            from link in db.SiteModuleItems.AsNoTracking()
            join item in db.SiteItems.AsNoTracking() on link.ItemId equals item.Id
            where moduleIds.Contains(link.ModuleId) && link.IsActive && item.IsActive
            select new { link.ModuleId, Item = item })
            .ToListAsync(ct))
            .GroupBy(x => x.ModuleId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.Item.Id, x => x.Item));

        // B-S-H2: clear only the values the caller could have sent -- those of items currently on the
        // record's form. A value captured against an item since detached (or retired) is outside this
        // set; the caller cannot resend it, so deleting it here would lose it for good, and it must
        // survive to reappear when the item is reattached. One query for every record; the per-module
        // filter is applied below, so a value is removed only if ITS record's module allows the item.
        var recordIds = pending.Select(p => p.Record.Id).Distinct().ToList();
        var allowedItemIds = allowedByModule.Values.SelectMany(items => items.Keys).Distinct().ToList();

        List<SiteRecordValue> stored = allowedItemIds.Count == 0
            ? []
            : await db.SiteRecordValues
                .Where(v => recordIds.Contains(v.RecordId) && allowedItemIds.Contains(v.ItemId))
                .ToListAsync(ct);

        var moduleByRecord = pending
            .GroupBy(p => p.Record.Id)
            .ToDictionary(g => g.Key, g => g.First().Record.ModuleId);

        db.SiteRecordValues.RemoveRange(stored.Where(v =>
            allowedByModule.TryGetValue(moduleByRecord[v.RecordId], out Dictionary<Guid, SiteItem>? items)
            && items.ContainsKey(v.ItemId)));

        var optionItemIds = pending
            .SelectMany(p => p.Values)
            .Where(v => v.OptionId.HasValue)
            .Select(v => v.ItemId)
            .Distinct()
            .ToList();

        Dictionary<Guid, HashSet<Guid>> optionsByItem = optionItemIds.Count == 0
            ? []
            : (await db.SiteItemOptions.AsNoTracking()
                    .Where(o => optionItemIds.Contains(o.ItemId) && o.IsActive)
                    .Select(o => new { o.ItemId, o.Id })
                    .ToListAsync(ct))
                .GroupBy(o => o.ItemId)
                .ToDictionary(g => g.Key, g => g.Select(o => o.Id).ToHashSet());

        foreach ((SiteRecord record, IReadOnlyList<SiteValueRequest> values) in pending)
        {
            Dictionary<Guid, SiteItem> allowed =
                allowedByModule.TryGetValue(record.ModuleId, out Dictionary<Guid, SiteItem>? items) ? items : [];

            Result staged = Stage(db, record, values, allowed, optionsByItem);
            if (staged.IsFailure)
            {
                return staged;
            }
        }

        return Result.Success();
    }

    private static Result Stage(
        SitesDbContext db, SiteRecord record, IReadOnlyList<SiteValueRequest> values,
        Dictionary<Guid, SiteItem> allowed, Dictionary<Guid, HashSet<Guid>> optionsByItem)
    {
        foreach (SiteValueRequest value in values)
        {
            if (!allowed.TryGetValue(value.ItemId, out SiteItem? item))
            {
                return SiteErrors.LinkNotFound;
            }

            Result<SiteRecordValue> created = SiteRecordValue.Create(
                record.Id, value.ItemId, item.DataType, value.ValueNumber, value.ValueText,
                value.ValueBool, value.ValueDate, value.OptionId);

            if (created.IsFailure)
            {
                return created.Error;
            }

            // The item's own bounds, length, pattern and option membership -- the pure rule that moved
            // onto the entity at S1-03. The item comes from the batch loaded above, never a per-value
            // query: this loop runs once per cell of a grid.
            Result validated = item.ValidateValue(
                value.ValueNumber, value.ValueText, value.OptionId,
                optionsByItem.TryGetValue(value.ItemId, out HashSet<Guid>? options) ? options : null);

            if (validated.IsFailure)
            {
                return validated;
            }

            db.SiteRecordValues.Add(created.Value!);
        }

        return Result.Success();
    }
}
