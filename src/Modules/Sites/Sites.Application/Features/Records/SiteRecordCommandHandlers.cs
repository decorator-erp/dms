using BuildingBlocks.Messaging;
using Crm.Contracts;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SharedKernel;
using Sites.Application.Features.Surveys;
using Sites.Contracts;
using Sites.Contracts.Records;
using Sites.Domain;
using Sites.Infrastructure.Persistence;

namespace Sites.Application.Features.Records;

/// <summary>S9: the <c>__</c> prefix is reserved for <see cref="SiteRecord.ParkLabel"/>'s placeholders.</summary>
internal static class SiteRecordLabelRules
{
    public const string ReservedLabelMessage = "A record label may not start with \"__\".";

    public static bool NotReserved(string? label) =>
        label is null || !label.TrimStart().StartsWith("__", StringComparison.Ordinal);
}

internal sealed class AddSiteRecordCommandValidator : AbstractValidator<AddSiteRecordCommand>
{
    public AddSiteRecordCommandValidator()
    {
        RuleFor(x => x.SiteId).NotEmpty();
        RuleFor(x => x.AppUserId).NotEmpty();
        RuleFor(x => x.Request.ModuleId).NotEmpty();
        RuleFor(x => x.Request.Label).NotEmpty().MaximumLength(200).Must(SiteRecordLabelRules.NotReserved)
            .WithMessage(SiteRecordLabelRules.ReservedLabelMessage);
        RuleFor(x => x.Request.SortOrder).GreaterThanOrEqualTo(0);
    }
}

internal sealed class AddSiteRecordCommandHandler(
    SitesDbContext db, ICrmSpaceReader spaces, ICrmCatalogItemReader catalog)
    : ICommandHandler<AddSiteRecordCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(
        AddSiteRecordCommand command, CancellationToken ct = default)
    {
        Site? site = await SiteWrites.LoadForWriteAsync(db, command.SiteId, ct);
        if (site is null)
        {
            return SiteErrors.NotFound;
        }

        if (!site.IsEditable)
        {
            return SiteErrors.SiteLocked;
        }

        // S12: a retired module takes no new records. It keeps its fields, so nothing downstream notices.
        if (!await db.SiteModules.AnyAsync(m => m.Id == command.Request.ModuleId && m.IsActive, ct))
        {
            return SiteErrors.ModuleNotFound;
        }

        Result space = await SiteWrites.ValidateSpaceAsync(
            spaces, command.Request.SpaceId, site.ProjectId, ct);
        if (space.IsFailure)
        {
            return space.Error;
        }

        Result catalogItem = await SiteWrites.ValidateCatalogItemAsync(catalog, command.Request.CatalogItemId, ct);
        if (catalogItem.IsFailure)
        {
            return catalogItem.Error;
        }

        Result label = await SiteRecordWrites.EnsureLabelFreeAsync(
            db, command.SiteId, command.Request.ModuleId, command.Request.Label, null, ct);
        if (label.IsFailure)
        {
            return label.Error;
        }

        Result<SiteRecord> record = SiteRecord.Create(
            command.SiteId, command.Request.ModuleId, command.Request.Label, command.Request.SpaceId,
            command.Request.CatalogItemId, command.Request.SortOrder, command.Request.Note);

        if (record.IsFailure)
        {
            return record.Error;
        }

        db.SiteRecords.Add(record.Value!);

        Result saved = await SiteWrites.SaveGuardedAsync(db, site, ct);
        return saved.IsFailure ? saved.Error : record.Value!.Id;
    }
}

/// <summary>Defect B8's record half — the backend has no validator for this command, so a 201-character
/// label was a 500 from the column rather than a 400 naming the field.</summary>
internal sealed class UpdateSiteRecordCommandValidator : AbstractValidator<UpdateSiteRecordCommand>
{
    public UpdateSiteRecordCommandValidator()
    {
        RuleFor(x => x.SiteId).NotEmpty();
        RuleFor(x => x.RecordId).NotEmpty();
        RuleFor(x => x.AppUserId).NotEmpty();
        RuleFor(x => x.Request.Label).NotEmpty().MaximumLength(200).Must(SiteRecordLabelRules.NotReserved)
            .WithMessage(SiteRecordLabelRules.ReservedLabelMessage);
        RuleFor(x => x.Request.SortOrder).GreaterThanOrEqualTo(0);
    }
}

internal sealed class UpdateSiteRecordCommandHandler(
    SitesDbContext db, ICrmSpaceReader spaces, ICrmCatalogItemReader catalog)
    : ICommandHandler<UpdateSiteRecordCommand>
{
    public async Task<Result> HandleAsync(UpdateSiteRecordCommand command, CancellationToken ct = default)
    {
        Result<(Site Site, SiteRecord Record)> loaded = await SiteRecordWrites.LoadEditableAsync(db, command.SiteId, command.RecordId, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        (Site? site, SiteRecord? record) = loaded.Value;

        Result space = await SiteWrites.ValidateSpaceAsync(spaces, command.Request.SpaceId, site.ProjectId, ct);
        if (space.IsFailure)
        {
            return space;
        }

        Result catalogItem = await SiteWrites.ValidateCatalogItemAsync(catalog, command.Request.CatalogItemId, ct);
        if (catalogItem.IsFailure)
        {
            return catalogItem;
        }

        Result label = await SiteRecordWrites.EnsureLabelFreeAsync(
            db, command.SiteId, record.ModuleId, command.Request.Label, record.Id, ct);
        if (label.IsFailure)
        {
            return label;
        }

        Result updated = record.Update(
            command.Request.Label, command.Request.SpaceId, command.Request.CatalogItemId,
            command.Request.SortOrder, command.Request.Note);

        if (updated.IsFailure)
        {
            return updated;
        }

        return await SiteWrites.SaveGuardedAsync(db, site, ct);
    }
}

internal sealed class DeleteSiteRecordCommandHandler(SitesDbContext db)
    : ICommandHandler<DeleteSiteRecordCommand>
{
    public async Task<Result> HandleAsync(DeleteSiteRecordCommand command, CancellationToken ct = default)
    {
        Result<(Site Site, SiteRecord Record)> loaded = await SiteRecordWrites.LoadEditableAsync(db, command.SiteId, command.RecordId, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        // Soft: the label frees for reuse because ux_site_records_label is partial on IsActive.
        loaded.Value.Record.Deactivate();

        return await SiteWrites.SaveGuardedAsync(db, loaded.Value.Site, ct);
    }
}

internal sealed class SetSiteRecordValuesCommandValidator : AbstractValidator<SetSiteRecordValuesCommand>
{
    public SetSiteRecordValuesCommandValidator()
    {
        RuleFor(x => x.SiteId).NotEmpty();
        RuleFor(x => x.RecordId).NotEmpty();
        RuleFor(x => x.AppUserId).NotEmpty();

        // Defect B8: the backend has no validator here at all, so a null Values collection was a null
        // reference inside the handler and came back as an unexplained 500.
        RuleFor(x => x.Request.Values).NotNull();
        RuleForEach(x => x.Request.Values).SetValidator(new SiteValueRequestValidator());
    }
}

internal sealed class SetSiteRecordValuesCommandHandler(SitesDbContext db, ISiteModuleValidator validator)
    : ICommandHandler<SetSiteRecordValuesCommand>
{
    public async Task<Result> HandleAsync(
        SetSiteRecordValuesCommand command, CancellationToken ct = default)
    {
        Result<(Site Site, SiteRecord Record)> loaded = await SiteRecordWrites.LoadEditableAsync(db, command.SiteId, command.RecordId, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        SiteRecord record = loaded.Value.Record;

        // The module comes from the STORED record, never from the caller. Validating against a module the
        // caller named would answer someone who has no business knowing that module's shape -- and, worse,
        // would let them pick a module with no required fields to bypass required-field validation.
        Result validated = await validator.ValidateAsync(record.ModuleId, command.Request.Values, ct);
        if (validated.IsFailure)
        {
            return validated;
        }

        return await SiteRecordWrites.ReplaceValuesAsync(db, loaded.Value.Site, record, command.Request.Values, ct);
    }
}

internal sealed class BulkSaveSiteRecordsCommandValidator : AbstractValidator<BulkSaveSiteRecordsCommand>
{
    /// <summary>The cap is new. The backend accepts an unbounded record list inside an open transaction,
    /// so one request could hold it open for arbitrarily long; 500 is far above any real grid.</summary>
    public const int MaxRecordsPerBatch = 500;

    public BulkSaveSiteRecordsCommandValidator()
    {
        RuleFor(x => x.SiteId).NotEmpty();
        RuleFor(x => x.AppUserId).NotEmpty();
        RuleFor(x => x.Request.Records).NotNull();
        RuleFor(x => x.Request.Records.Count)
            .LessThanOrEqualTo(MaxRecordsPerBatch)
            .When(x => x.Request.Records is not null);

        // S4: one row per stored record. Two rows naming the same RecordId would both apply to one
        // tracked record, the second staging its values over the first's.
        RuleFor(x => x.Request.Records)
            .Must(rows => rows
                .Where(r => r?.RecordId is not null)
                .GroupBy(r => r.RecordId)
                .All(g => g.Count() == 1))
            .WithMessage("RecordIds in a bulk save must be distinct.")
            .When(x => x.Request.Records is not null);

        RuleForEach(x => x.Request.Records).ChildRules(row =>
        {
            row.RuleFor(r => r.Label).NotEmpty().MaximumLength(200)
                .Must(SiteRecordLabelRules.NotReserved).WithMessage(SiteRecordLabelRules.ReservedLabelMessage);
            row.RuleFor(r => r.SortOrder).GreaterThanOrEqualTo(0);
            row.RuleFor(r => r.Values).NotNull();
            row.RuleForEach(r => r.Values).SetValidator(new SiteValueRequestValidator());
        });
    }
}

/// <summary>
/// The tablet's save button: a whole module's grid in one shot, one transaction — a surveyor who loses
/// signal mid-save must not leave half a room measured.
/// </summary>
internal sealed class BulkSaveSiteRecordsCommandHandler(
    SitesDbContext db, ISiteModuleValidator validator, ICrmSpaceReader spaces, ICrmCatalogItemReader catalog)
    : ICommandHandler<BulkSaveSiteRecordsCommand>
{
    public async Task<Result> HandleAsync(
        BulkSaveSiteRecordsCommand command, CancellationToken ct = default)
    {
        Site? site = await SiteWrites.LoadForWriteAsync(db, command.SiteId, ct);
        if (site is null)
        {
            return SiteErrors.NotFound;
        }

        if (!site.IsEditable)
        {
            return SiteErrors.SiteLocked;
        }

        IReadOnlyList<BulkSiteRecordRequest> rows = command.Request.Records;

        // Defects B2 and B3. The backend validates each row against row.ModuleId -- the CLIENT's value --
        // and then writes against the stored record's module, because SiteRecord.Update cannot change it.
        // So an update row could name any module: values were judged by a schema that had nothing to do
        // with the record, and picking a module with no required fields skipped required-field validation
        // entirely. The same untrusted value keyed the in-batch duplicate-label check (B3), so two rows in
        // one grid could carry different ModuleIds, land in different groups, pass the dedupe, and then
        // collide on ux_site_records_label as an unhandled write failure.
        //
        // Resolved here, once: every row's module is the stored record's for updates and the request's
        // only for inserts. Everything downstream uses the resolved value.
        Dictionary<Guid, SiteRecord> existingRecords = await LoadExistingAsync(db, command.SiteId, rows, ct);

        var resolved = new List<ResolvedRow>(rows.Count);

        foreach (BulkSiteRecordRequest row in rows)
        {
            if (row.RecordId is { } recordId)
            {
                if (!existingRecords.TryGetValue(recordId, out SiteRecord? record))
                {
                    return SiteErrors.RecordNotFound;
                }

                resolved.Add(new ResolvedRow(row, record, record.ModuleId));
            }
            else
            {
                if (row.ModuleId == Guid.Empty)
                {
                    return SiteErrors.InvalidData;
                }

                resolved.Add(new ResolvedRow(row, null, row.ModuleId));
            }
        }

        // S12: insert rows may only name an ACTIVE module -- one query for the whole grid. Update rows keep
        // their stored record's module, whatever its state.
        var insertModuleIds = resolved
            .Where(r => r.Existing is null)
            .Select(r => r.ModuleId)
            .Distinct()
            .ToList();

        if (insertModuleIds.Count > 0)
        {
            int active = await db.SiteModules
                .CountAsync(m => insertModuleIds.Contains(m.Id) && m.IsActive, ct);

            if (active != insertModuleIds.Count)
            {
                return SiteErrors.ModuleNotFound;
            }
        }

        // S7: one validator pass for the whole grid -- a fixed number of queries, never one per row. The
        // first failing row, in grid order, answers, exactly as the per-row loop did.
        IReadOnlyList<Result> validations = await validator.ValidateManyAsync(
            [.. resolved.Select(r => new SiteModuleValueSet(r.ModuleId, r.Request.Values))], ct);

        if (validations.FirstOrDefault(v => v.IsFailure) is { } invalid)
        {
            return invalid;
        }

        // Two rows of one grid claiming the same label within a module. The stored-label check cannot see
        // it -- neither row is written yet -- so unguarded it reaches the unique index as an unhandled
        // write failure. Keyed on the RESOLVED module, per B3.
        bool duplicate = resolved
            .GroupBy(r => (r.ModuleId, Label: SiteRecordWrites.Normalize(r.Request.Label)))
            .Any(g => g.Count() > 1);

        if (duplicate)
        {
            return SiteErrors.LabelTaken;
        }

        // Validation pass: nothing below touches the change tracker until every row has passed.
        //
        // S9: the stored-label check excludes EVERY record the batch rewrites, not just the row's own --
        // their labels are about to change, and the in-batch duplicate check above already judges them
        // against each other. Excluding only the row's own record made a two-row label swap "taken".
        var batchRecordIds = resolved
            .Where(r => r.Existing is not null)
            .Select(r => r.Existing!.Id)
            .ToList();

        // S7: spaces, catalog items and stored labels each read ONCE for the whole grid, then judged row
        // by row in grid order -- so the first failing row answers with the same error as before.
        var spaceIds = resolved
            .Where(r => r.Request.SpaceId.HasValue)
            .Select(r => r.Request.SpaceId!.Value)
            .Distinct()
            .ToList();
        Dictionary<Guid, CrmSpaceSummary> spacesById = spaceIds.Count == 0
            ? []
            : (await spaces.GetManyAsync(spaceIds, ct)).ToDictionary(s => s.Id);

        var catalogItemIds = resolved
            .Where(r => r.Request.CatalogItemId.HasValue)
            .Select(r => r.Request.CatalogItemId!.Value)
            .Distinct()
            .ToList();
        Dictionary<Guid, CrmCatalogItemSummary> catalogById = catalogItemIds.Count == 0
            ? []
            : (await catalog.GetManyAsync(catalogItemIds, ct)).ToDictionary(c => c.Id);

        HashSet<(Guid ModuleId, string Label)> taken = await SiteRecordWrites.TakenLabelsAsync(
            db, command.SiteId, [.. resolved.Select(r => (r.ModuleId, r.Request.Label))], batchRecordIds, ct);

        foreach (ResolvedRow row in resolved)
        {
            if (row.Request.SpaceId is { } spaceId)
            {
                Result space = SiteWrites.CheckSpace(spacesById.GetValueOrDefault(spaceId), site.ProjectId);
                if (space.IsFailure)
                {
                    return space;
                }
            }

            if (row.Request.CatalogItemId is { } catalogItemId)
            {
                Result catalogItem = SiteWrites.CheckCatalogItem(catalogById.GetValueOrDefault(catalogItemId));
                if (catalogItem.IsFailure)
                {
                    return catalogItem;
                }
            }

            if (taken.Contains((row.ModuleId, SiteRecordWrites.Normalize(row.Request.Label))))
            {
                return SiteErrors.LabelTaken;
            }
        }

        // S9: ux_site_records_label is checked per statement, so a swap (A->B, B->A) or a label freed by
        // one row and taken by another cannot land in one UPDATE batch. Every stored record whose label
        // changes is first parked under a unique placeholder and flushed, then the final labels are
        // written -- both passes in ONE transaction, so a failure anywhere rolls the parking back too.
        // That transaction is normally DomainEventDispatchDecorator's, which wraps every command and
        // rolls back on a failed Result; one is opened here only when none is ambient.
        var renamed = resolved
            .Where(r => r.Existing is { } e
                        && !string.Equals(e.Label, r.Request.Label.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Existing!)
            .ToList();

        await using IDbContextTransaction? transaction = renamed.Count > 0 && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

        if (renamed.Count > 0)
        {
            foreach (SiteRecord record in renamed)
            {
                record.ParkLabel();
            }

            Result parked = await SiteWrites.SaveGuardedAsync(db, site, ct);
            if (parked.IsFailure)
            {
                return parked;
            }
        }

        // Staging pass. The records are tracked entities, so the value pass works from them directly and
        // never queries for an unsaved insert.
        var pending = new List<(SiteRecord Record, IReadOnlyList<SiteValueRequest> Values)>(resolved.Count);

        foreach (ResolvedRow row in resolved)
        {
            if (row.Existing is { } existing)
            {
                Result updated = existing.Update(
                    row.Request.Label, row.Request.SpaceId, row.Request.CatalogItemId,
                    row.Request.SortOrder, row.Request.Note);

                if (updated.IsFailure)
                {
                    return updated;
                }

                pending.Add((existing, row.Request.Values));
            }
            else
            {
                Result<SiteRecord> created = SiteRecord.Create(
                    command.SiteId, row.ModuleId, row.Request.Label, row.Request.SpaceId,
                    row.Request.CatalogItemId, row.Request.SortOrder, row.Request.Note);

                if (created.IsFailure)
                {
                    return created.Error;
                }

                db.SiteRecords.Add(created.Value!);
                pending.Add((created.Value!, row.Request.Values));
            }
        }

        // The records are tracked entities, so the value pass works from them directly. Ids are
        // client-generated UUIDv7, so a value can reference a record that has not been written yet.
        // S7: staged set-based, a fixed number of queries for the whole grid.
        Result set = await SiteRecordWrites.StageValuesManyAsync(db, pending, ct);
        if (set.IsFailure)
        {
            return set;
        }

        // Every early return leaves the database untouched: either nothing was flushed yet, or the
        // parking pass is rolled back with the transaction.
        Result saved = await SiteWrites.SaveGuardedAsync(db, site, pending.Select(p => p.Record), ct);
        if (saved.IsSuccess && transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }

        return saved;
    }

    private static async Task<Dictionary<Guid, SiteRecord>> LoadExistingAsync(
        SitesDbContext db, Guid siteId, IReadOnlyList<BulkSiteRecordRequest> rows, CancellationToken ct)
    {
        var ids = rows.Where(r => r.RecordId.HasValue).Select(r => r.RecordId!.Value).ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        // Scoped to the site: a record belonging to someone else's survey must not be findable here.
        return await db.SiteRecords
            .Where(r => r.SiteId == siteId && ids.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, ct);
    }

    private sealed record ResolvedRow(BulkSiteRecordRequest Request, SiteRecord? Existing, Guid ModuleId);
}
