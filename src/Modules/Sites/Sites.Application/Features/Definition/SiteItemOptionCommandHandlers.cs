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

internal sealed class AddSiteItemOptionCommandValidator : AbstractValidator<AddSiteItemOptionCommand>
{
    public AddSiteItemOptionCommandValidator()
    {
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Request.NameEn).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Request.NameAr).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Request.SortOrder).GreaterThanOrEqualTo(0);
    }
}

internal sealed class AddSiteItemOptionCommandHandler(SitesDbContext db, ISiteSchemaCache schemaCache)
    : ICommandHandler<AddSiteItemOptionCommand, Guid>
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1304:Specify CultureInfo",
        Justification = "The column's ToLower() inside the query runs as SQL lower(); ToLowerInvariant and the culture overloads do not translate.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1311:Specify a culture or use an invariant version",
        Justification = "Same query: translated to SQL lower(), never run in .NET.")]
    public async Task<Result<Guid>> HandleAsync(
        AddSiteItemOptionCommand command, CancellationToken ct = default)
    {
        SiteItem? item = await db.SiteItems.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == command.ItemId, ct);

        if (item is null)
        {
            return SiteErrors.ItemNotFound;
        }

        // A rule about the item, so arguably entity-shaped -- but it decides whether a DIFFERENT
        // aggregate may be created, which is a handler's call. Options on a non-Select item would be
        // stored, never rendered, and never selectable.
        if (item.DataType != Sites.Domain.SiteItemDataType.Select)
        {
            return SiteErrors.OptionsRequireSelect;
        }

        string normalized = command.Request.Code.Trim().ToLowerInvariant();
        bool taken = await db.SiteItemOptions
            .AnyAsync(o => o.ItemId == command.ItemId && o.Code.ToLower() == normalized, ct);

        if (taken)
        {
            return SiteErrors.CodeTaken;
        }

        Result<SiteItemOption> option = SiteItemOption.Create(
            command.ItemId, command.Request.Code, command.Request.NameEn,
            command.Request.NameAr, command.Request.SortOrder);

        if (option.IsFailure)
        {
            return option.Error;
        }

        db.SiteItemOptions.Add(option.Value!);

        Result saved = await SiteDefinitionWrites.SaveAsync(db, schemaCache, ct);
        return saved.IsFailure ? saved.Error : option.Value!.Id;
    }
}

/// <summary>Defect B8's fix — the backend has no validator for this command.</summary>
internal sealed class UpdateSiteItemOptionCommandValidator : AbstractValidator<UpdateSiteItemOptionCommand>
{
    public UpdateSiteItemOptionCommandValidator()
    {
        RuleFor(x => x.OptionId).NotEmpty();
        RuleFor(x => x.Request.NameEn).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Request.NameAr).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Request.SortOrder).GreaterThanOrEqualTo(0);
    }
}

internal sealed class UpdateSiteItemOptionCommandHandler(SitesDbContext db, ISiteSchemaCache schemaCache)
    : ICommandHandler<UpdateSiteItemOptionCommand>
{
    public async Task<Result> HandleAsync(
        UpdateSiteItemOptionCommand command, CancellationToken ct = default)
    {
        SiteItemOption? option = await db.SiteItemOptions.FirstOrDefaultAsync(o => o.Id == command.OptionId, ct);
        if (option is null)
        {
            return SiteErrors.OptionNotFound;
        }

        Result updated = option.Update(
            command.Request.NameEn, command.Request.NameAr, command.Request.SortOrder);

        if (updated.IsFailure)
        {
            return updated;
        }

        return await SiteDefinitionWrites.SaveAsync(db, schemaCache, ct);
    }
}

internal sealed class SetSiteItemOptionActiveCommandHandler(SitesDbContext db, ISiteSchemaCache schemaCache)
    : ICommandHandler<SetSiteItemOptionActiveCommand>
{
    public async Task<Result> HandleAsync(
        SetSiteItemOptionActiveCommand command, CancellationToken ct = default)
    {
        SiteItemOption? option = await db.SiteItemOptions.FirstOrDefaultAsync(o => o.Id == command.OptionId, ct);
        if (option is null)
        {
            return SiteErrors.OptionNotFound;
        }

        if (command.IsActive)
        {
            option.Activate();
        }
        else
        {
            option.Deactivate();
        }

        return await SiteDefinitionWrites.SaveAsync(db, schemaCache, ct);
    }
}
