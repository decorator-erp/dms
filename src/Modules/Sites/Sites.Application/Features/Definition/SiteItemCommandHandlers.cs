using BuildingBlocks.Messaging;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Sites.Contracts;
using Sites.Contracts.Definition;
using Sites.Domain;
using Sites.Domain.Definition;
using Sites.Infrastructure.Persistence;

namespace Sites.Application.Features.Definition;

internal sealed class CreateSiteItemCommandValidator : AbstractValidator<CreateSiteItemCommand>
{
    public CreateSiteItemCommandValidator()
    {
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Request.NameEn).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Request.NameAr).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Request.Unit).MaximumLength(32);
        RuleFor(x => x.Request.RegexPattern).MaximumLength(256);
    }
}

internal sealed class CreateSiteItemCommandHandler(SitesDbContext db, ISiteSchemaCache schemaCache)
    : ICommandHandler<CreateSiteItemCommand, Guid>
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1304:Specify CultureInfo",
        Justification = "The column's ToLower() inside the query runs as SQL lower(); ToLowerInvariant and the culture overloads do not translate.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1311:Specify a culture or use an invariant version",
        Justification = "Same query: translated to SQL lower(), never run in .NET.")]
    public async Task<Result<Guid>> HandleAsync(
        CreateSiteItemCommand command, CancellationToken ct = default)
    {
        string normalized = command.Request.Code.Trim().ToLowerInvariant();

        if (await db.SiteItems.AnyAsync(i => i.Code.ToLower() == normalized, ct))
        {
            return SiteErrors.CodeTaken;
        }

        Result<SiteItem> item = SiteItem.Create(
            command.Request.Code, command.Request.NameEn, command.Request.NameAr,
            SiteEnumMapping.ToDomain(command.Request.DataType), command.Request.Unit,
            command.Request.MinValue, command.Request.MaxValue, command.Request.Tolerance,
            command.Request.MaxLength, command.Request.RegexPattern);

        if (item.IsFailure)
        {
            return item.Error;
        }

        db.SiteItems.Add(item.Value!);

        Result saved = await SiteDefinitionWrites.SaveAsync(db, schemaCache, ct);
        return saved.IsFailure ? saved.Error : item.Value!.Id;
    }
}

/// <summary>Defect B8's fix — the backend has no validator for this command.</summary>
internal sealed class UpdateSiteItemCommandValidator : AbstractValidator<UpdateSiteItemCommand>
{
    public UpdateSiteItemCommandValidator()
    {
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.Request.NameEn).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Request.NameAr).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Request.RegexPattern).MaximumLength(256);
    }
}

internal sealed class UpdateSiteItemCommandHandler(SitesDbContext db, ISiteSchemaCache schemaCache)
    : ICommandHandler<UpdateSiteItemCommand>
{
    public async Task<Result> HandleAsync(UpdateSiteItemCommand command, CancellationToken ct = default)
    {
        SiteItem? item = await db.SiteItems.FirstOrDefaultAsync(i => i.Id == command.ItemId, ct);
        if (item is null)
        {
            return SiteErrors.ItemNotFound;
        }

        Result updated = item.Update(
            command.Request.NameEn, command.Request.NameAr, command.Request.MinValue,
            command.Request.MaxValue, command.Request.MaxLength, command.Request.RegexPattern);

        if (updated.IsFailure)
        {
            return updated;
        }

        return await SiteDefinitionWrites.SaveAsync(db, schemaCache, ct);
    }
}

/// <summary>Defect B8's fix — the backend has no validator for this command either.</summary>
internal sealed class ChangeSiteItemShapeCommandValidator : AbstractValidator<ChangeSiteItemShapeCommand>
{
    public ChangeSiteItemShapeCommandValidator()
    {
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Request.Unit).MaximumLength(32);
    }
}

/// <summary>
/// The governance path: the four fields that decide how every stored value is read and grouped, frozen
/// the moment any value references the item.
/// </summary>
internal sealed class ChangeSiteItemShapeCommandHandler(SitesDbContext db, ISiteSchemaCache schemaCache)
    : ICommandHandler<ChangeSiteItemShapeCommand>
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1304:Specify CultureInfo",
        Justification = "The column's ToLower() inside the query runs as SQL lower(); ToLowerInvariant and the culture overloads do not translate.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1311:Specify a culture or use an invariant version",
        Justification = "Same query: translated to SQL lower(), never run in .NET.")]
    public async Task<Result> HandleAsync(
        ChangeSiteItemShapeCommand command, CancellationToken ct = default)
    {
        SiteItem? item = await db.SiteItems.FirstOrDefaultAsync(i => i.Id == command.ItemId, ct);
        if (item is null)
        {
            return SiteErrors.ItemNotFound;
        }

        // The one probe that makes this whole rule real. Not filtered by the record's IsActive: a value
        // on a soft-deleted record still pins the item's meaning for that historical row.
        bool hasValues = await db.SiteRecordValues.AnyAsync(v => v.ItemId == command.ItemId, ct);
        if (hasValues)
        {
            return SiteErrors.ItemLocked;
        }

        // Skip the uniqueness probe when the "rename" changes nothing -- otherwise an unchanged code
        // collides with the item's own row.
        if (!item.CodeEquals(command.Request.Code))
        {
            string normalized = command.Request.Code.Trim().ToLowerInvariant();
            if (await db.SiteItems.AnyAsync(i => i.Id != command.ItemId && i.Code.ToLower() == normalized, ct))
            {
                return SiteErrors.CodeTaken;
            }
        }

        Result changed = item.ChangeCapturedShape(
            command.Request.Code, SiteEnumMapping.ToDomain(command.Request.DataType),
            command.Request.Unit, command.Request.Tolerance);

        if (changed.IsFailure)
        {
            return changed;
        }

        return await SiteDefinitionWrites.SaveAsync(db, schemaCache, ct);
    }
}

internal sealed class DeleteSiteItemCommandHandler(SitesDbContext db, ISiteSchemaCache schemaCache)
    : ICommandHandler<DeleteSiteItemCommand>
{
    public async Task<Result> HandleAsync(DeleteSiteItemCommand command, CancellationToken ct = default)
    {
        SiteItem? item = await db.SiteItems.FirstOrDefaultAsync(i => i.Id == command.ItemId, ct);
        if (item is null)
        {
            return SiteErrors.ItemNotFound;
        }

        Result deletable = item.EnsureDeletable();
        if (deletable.IsFailure)
        {
            return deletable;
        }

        if (await db.SiteRecordValues.AnyAsync(v => v.ItemId == command.ItemId, ct))
        {
            return SiteErrors.ItemInUse;
        }

        // AnyAsync, not GetAllAsync(...).Any(): the backend materializes every link for the item just to
        // test whether one exists. Same answer, one row instead of all of them.
        if (await db.SiteModuleItems.AnyAsync(l => l.ItemId == command.ItemId, ct))
        {
            return SiteErrors.ItemInUse;
        }

        db.SiteItems.Remove(item);

        return await SiteDefinitionWrites.SaveAsync(db, schemaCache, ct);
    }
}

internal sealed class SetSiteItemActiveCommandHandler(SitesDbContext db, ISiteSchemaCache schemaCache)
    : ICommandHandler<SetSiteItemActiveCommand>
{
    public async Task<Result> HandleAsync(SetSiteItemActiveCommand command, CancellationToken ct = default)
    {
        SiteItem? item = await db.SiteItems.FirstOrDefaultAsync(i => i.Id == command.ItemId, ct);
        if (item is null)
        {
            return SiteErrors.ItemNotFound;
        }

        if (command.IsActive)
        {
            item.Activate();
        }
        else
        {
            item.Deactivate();
        }

        return await SiteDefinitionWrites.SaveAsync(db, schemaCache, ct);
    }
}
