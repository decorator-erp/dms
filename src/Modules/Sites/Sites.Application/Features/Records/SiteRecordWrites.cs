using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Sites.Application.Features.Surveys;
using Sites.Contracts.Records;
using Sites.Domain;
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
        var site = await SiteWrites.LoadForWriteAsync(db, siteId, ct);
        if (site is null) return SiteErrors.NotFound;
        if (!site.IsEditable) return SiteErrors.SiteLocked;

        var record = await SiteWrites.LoadRecordForWriteAsync(db, siteId, recordId, ct);
        if (record.IsFailure) return record.Error;

        return (site, record.Value!);
    }

    /// <summary>
    /// Case-insensitive label uniqueness within (site, module), among ACTIVE rows — mirroring
    /// <c>ux_site_records_label</c>, so the caller gets a 409 rather than a constraint violation.
    /// </summary>
    public static async Task<Result> EnsureLabelFreeAsync(
        SitesDbContext db, Guid siteId, Guid moduleId, string label, Guid? excludingRecordId,
        CancellationToken ct)
    {
        var normalized = label.Trim().ToLower();

        var taken = await db.SiteRecords
            .AnyAsync(
                r => r.SiteId == siteId
                     && r.ModuleId == moduleId
                     && r.Label.ToLower() == normalized
                     && (excludingRecordId == null || r.Id != excludingRecordId),
                ct);

        return taken ? SiteErrors.LabelTaken : Result.Success();
    }

    /// <summary>Stages a record's whole value set, then saves.</summary>
    public static async Task<Result> ReplaceValuesAsync(
        SitesDbContext db, SiteRecord record, IReadOnlyList<SiteValueRequest> values, CancellationToken ct)
    {
        var staged = await StageValuesAsync(db, record, values, ct);
        if (staged.IsFailure) return staged;

        return await SiteWrites.SaveAsync(db, ct);
    }

    /// <summary>
    /// Replaces a record's value set on the change tracker without saving, so the grid save can stage
    /// many records inside one transaction.
    ///
    /// <para>Delete-all-then-insert, because a value absent from the incoming set is <b>cleared</b>: the
    /// table has no soft-delete flag, and a cleared measurement is not a measurement.</para>
    ///
    /// <para>Item types and options are loaded in two batched queries, never per value — the N+1 the
    /// backend's own domain service carries a comment about having removed.</para>
    /// </summary>
    public static async Task<Result> StageValuesAsync(
        SitesDbContext db, SiteRecord record, IReadOnlyList<SiteValueRequest> values, CancellationToken ct)
    {
        var existing = await db.SiteRecordValues
            .Where(v => v.RecordId == record.Id)
            .ToListAsync(ct);

        db.SiteRecordValues.RemoveRange(existing);

        if (values.Count == 0) return Result.Success();

        // Only items actually linked to this record's module may carry a value. The validator says the
        // same thing with a nicer message; this is the structural guard, and it is what stops a value
        // being written against a field the module does not have.
        var allowed = await (
            from link in db.SiteModuleItems.AsNoTracking()
            join item in db.SiteItems.AsNoTracking() on link.ItemId equals item.Id
            where link.ModuleId == record.ModuleId && link.IsActive && item.IsActive
            select item)
            .ToDictionaryAsync(i => i.Id, ct);

        var optionItemIds = values
            .Where(v => v.OptionId.HasValue)
            .Select(v => v.ItemId)
            .Distinct()
            .ToList();

        var optionsByItem = optionItemIds.Count == 0
            ? []
            : (await db.SiteItemOptions.AsNoTracking()
                    .Where(o => optionItemIds.Contains(o.ItemId) && o.IsActive)
                    .Select(o => new { o.ItemId, o.Id })
                    .ToListAsync(ct))
                .GroupBy(o => o.ItemId)
                .ToDictionary(g => g.Key, g => g.Select(o => o.Id).ToHashSet());

        foreach (var value in values)
        {
            if (!allowed.TryGetValue(value.ItemId, out var item)) return SiteErrors.LinkNotFound;

            var created = SiteRecordValue.Create(
                record.Id, value.ItemId, item.DataType, value.ValueNumber, value.ValueText,
                value.ValueBool, value.ValueDate, value.OptionId);

            if (created.IsFailure) return created.Error;

            // The item's own bounds, length, pattern and option membership -- the pure rule that moved
            // onto the entity at S1-03. The item comes from the batch loaded above, never a per-value
            // query: this loop runs once per cell of a grid.
            var validated = item.ValidateValue(
                value.ValueNumber, value.ValueText, value.OptionId,
                optionsByItem.TryGetValue(value.ItemId, out var options) ? options : null);

            if (validated.IsFailure) return validated;

            db.SiteRecordValues.Add(created.Value!);
        }

        return Result.Success();
    }
}
