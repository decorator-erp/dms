using BuildingBlocks.Messaging;
using Crm.Contracts;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Sites.Application.Features.Surveys;
using Sites.Contracts;
using Sites.Contracts.Records;
using Sites.Domain;
using Sites.Infrastructure.Persistence;

namespace Sites.Application.Features.Records;

internal sealed class AddSiteRecordCommandValidator : AbstractValidator<AddSiteRecordCommand>
{
    public AddSiteRecordCommandValidator()
    {
        RuleFor(x => x.SiteId).NotEmpty();
        RuleFor(x => x.AppUserId).NotEmpty();
        RuleFor(x => x.Request.ModuleId).NotEmpty();
        RuleFor(x => x.Request.Label).NotEmpty().MaximumLength(200);
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

        if (!await db.SiteModules.AnyAsync(m => m.Id == command.Request.ModuleId, ct))
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
        RuleFor(x => x.Request.Label).NotEmpty().MaximumLength(200);
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
            row.RuleFor(r => r.Label).NotEmpty().MaximumLength(200);
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

        foreach (ResolvedRow row in resolved)
        {
            Result validated = await validator.ValidateAsync(row.ModuleId, row.Request.Values, ct);
            if (validated.IsFailure)
            {
                return validated;
            }
        }

        // Two rows of one grid claiming the same label within a module. The stored-label check cannot see
        // it -- neither row is written yet -- so unguarded it reaches the unique index as an unhandled
        // write failure. Keyed on the RESOLVED module, per B3.
        bool duplicate = resolved
            .GroupBy(r => (r.ModuleId, Label: r.Request.Label.Trim().ToLowerInvariant()))
            .Any(g => g.Count() > 1);

        if (duplicate)
        {
            return SiteErrors.LabelTaken;
        }

        // No explicit transaction. Every record and every value is staged on the change tracker and
        // written by ONE SaveChangesAsync, which EF already wraps in a transaction -- so the grid is
        // all-or-nothing without one. The backend needs an explicit transaction only because its value
        // pass QUERIES for each record, which cannot see an unsaved insert; that forced a mid-transaction
        // flush, and its own comment records the bug that made the flush necessary. Working from the
        // tracked entities removes the flush, the second pass, and the transaction together.
        var pending = new List<(SiteRecord Record, IReadOnlyList<SiteValueRequest> Values)>(resolved.Count);

        foreach (ResolvedRow row in resolved)
        {
            Result space = await SiteWrites.ValidateSpaceAsync(spaces, row.Request.SpaceId, site.ProjectId, ct);
            if (space.IsFailure)
            {
                return space;
            }

            Result catalogItem = await SiteWrites.ValidateCatalogItemAsync(catalog, row.Request.CatalogItemId, ct);
            if (catalogItem.IsFailure)
            {
                return catalogItem;
            }

            Result stored = await SiteRecordWrites.EnsureLabelFreeAsync(
                db, command.SiteId, row.ModuleId, row.Request.Label, row.Existing?.Id, ct);
            if (stored.IsFailure)
            {
                return stored;
            }

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
        foreach ((SiteRecord? record, IReadOnlyList<SiteValueRequest>? values) in pending)
        {
            Result set = await SiteRecordWrites.StageValuesAsync(db, record, values, ct);
            if (set.IsFailure)
            {
                return set;
            }
        }

        // Nothing above this line has touched the database as a write. Every early return leaves the
        // change tracker dirty and the database untouched, and the scoped DbContext is discarded with the
        // request -- so "all or nothing" holds without an explicit transaction.
        return await SiteWrites.SaveGuardedAsync(db, site, pending.Select(p => p.Record), ct);
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
