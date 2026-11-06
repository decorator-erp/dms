using BuildingBlocks.Messaging;
using Crm.Contracts.Catalog;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SharedKernel;

namespace Crm.Application.Features.Catalog;

internal sealed class UpdateCrmCatalogEntryCommandValidator : AbstractValidator<UpdateCrmCatalogEntryCommand>
{
    /// <summary>FINDING-F-1: the limits are the columns' (items have 256-character names, the rest 128).</summary>
    public UpdateCrmCatalogEntryCommandValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64);
        RuleFor(x => x.NameEn).NotEmpty();
        RuleFor(x => x.NameAr).NotEmpty();
        RuleFor(x => x.NameEn).MaximumLength(256).When(x => x.Kind == CrmCatalogEntryKind.Item);
        RuleFor(x => x.NameAr).MaximumLength(256).When(x => x.Kind == CrmCatalogEntryKind.Item);
        RuleFor(x => x.NameEn).MaximumLength(128).When(x => x.Kind != CrmCatalogEntryKind.Item);
        RuleFor(x => x.NameAr).MaximumLength(128).When(x => x.Kind != CrmCatalogEntryKind.Item);
        RuleFor(x => x.SelectionMode).IsInEnum().When(x => x.SelectionMode is not null);
        RuleFor(x => x.Unit).MaximumLength(32);
        RuleFor(x => x.DescriptionEn).MaximumLength(1000);
        RuleFor(x => x.DescriptionAr).MaximumLength(1000);
        RuleFor(x => x.ImageUri).MaximumLength(1024);
    }
}

/// <summary>crm-complete F4 (D4): edits through each entity's own <c>Update</c> (which re-validates); a field
/// the request leaves out keeps its value.</summary>
internal sealed class UpdateCrmCatalogEntryCommandHandler(CrmDbContext db) : ICommandHandler<UpdateCrmCatalogEntryCommand>
{
    public async Task<Result> HandleAsync(UpdateCrmCatalogEntryCommand c, CancellationToken ct = default)
    {
        Result result;
        switch (c.Kind)
        {
            case CrmCatalogEntryKind.Category:
                CrmCatalogCategory? category = await db.CrmCatalogCategories.FirstOrDefaultAsync(x => x.Id == c.Id, ct);
                if (category is null)
                {
                    return CrmCatalogErrors.EntryNotFound;
                }

                result = category.Update(c.Code, c.NameEn, c.NameAr,
                    c.SelectionMode is { } mode ? CrmEnumMapping.ToDomain(mode) : category.SelectionMode,
                    c.IsRequired ?? category.IsRequired, c.DescriptionEn ?? category.DescriptionEn,
                    c.DescriptionAr ?? category.DescriptionAr, c.ImageUri ?? category.ImageUri);
                break;
            case CrmCatalogEntryKind.Item:
                CrmCatalogItem? item = await db.CrmCatalogItems.FirstOrDefaultAsync(x => x.Id == c.Id, ct);
                if (item is null)
                {
                    return CrmCatalogErrors.EntryNotFound;
                }

                result = item.Update(c.Code, c.NameEn, c.NameAr, c.IsPurchasable ?? item.IsPurchasable,
                    c.DescriptionEn ?? item.DescriptionEn, c.DescriptionAr ?? item.DescriptionAr, c.ImageUri ?? item.ImageUri);
                break;
            case CrmCatalogEntryKind.Attribute:
                CrmCatalogAttribute? attribute = await db.CrmCatalogAttributes.FirstOrDefaultAsync(x => x.Id == c.Id, ct);
                if (attribute is null)
                {
                    return CrmCatalogErrors.EntryNotFound;
                }

                result = attribute.Update(c.Code, c.NameEn, c.NameAr, c.Unit ?? attribute.Unit, c.IsRequired ?? attribute.IsRequired);
                break;
            default:
                CrmCatalogAttributeOption? option = await db.CrmCatalogAttributeOptions.FirstOrDefaultAsync(x => x.Id == c.Id, ct);
                if (option is null)
                {
                    return CrmCatalogErrors.EntryNotFound;
                }

                result = option.Update(c.Code, c.NameEn, c.NameAr, c.ImageUri ?? option.ImageUri);
                break;
        }

        if (result.IsFailure)
        {
            return result;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return CrmCatalogErrors.CodeTaken; // FINDING-F-1
        }
        catch (DbUpdateConcurrencyException)
        {
            return CrmCatalogErrors.EntryNotFound; // FINDING-F-8: deleted meanwhile
        }

        return Result.Success();
    }
}

/// <summary>F4: deactivate/reactivate. Always available (D4). Inactive entries drop out of pickers
/// (<c>tree?spaceTypeId</c>, the options list) but stay in history.</summary>
internal sealed class SetCrmCatalogEntryActiveCommandHandler(CrmDbContext db) : ICommandHandler<SetCrmCatalogEntryActiveCommand>
{
    public async Task<Result> HandleAsync(SetCrmCatalogEntryActiveCommand c, CancellationToken ct = default)
    {
        Entity? entry = c.Kind switch
        {
            CrmCatalogEntryKind.Category => await db.CrmCatalogCategories.FirstOrDefaultAsync(x => x.Id == c.Id, ct),
            CrmCatalogEntryKind.Item => await db.CrmCatalogItems.FirstOrDefaultAsync(x => x.Id == c.Id, ct),
            CrmCatalogEntryKind.Attribute => await db.CrmCatalogAttributes.FirstOrDefaultAsync(x => x.Id == c.Id, ct),
            _ => await db.CrmCatalogAttributeOptions.FirstOrDefaultAsync(x => x.Id == c.Id, ct),
        };
        switch (entry)
        {
            case null:
                return CrmCatalogErrors.EntryNotFound;
            case CrmCatalogCategory x:
                Toggle(x.Activate, x.Deactivate, c.IsActive);
                break;
            case CrmCatalogItem x:
                Toggle(x.Activate, x.Deactivate, c.IsActive);
                break;
            case CrmCatalogAttribute x:
                Toggle(x.Activate, x.Deactivate, c.IsActive);
                break;
            case CrmCatalogAttributeOption x:
                Toggle(x.Activate, x.Deactivate, c.IsActive);
                break;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return CrmCatalogErrors.EntryNotFound; // FINDING-F-8: deleted meanwhile
        }

        return Result.Success();
    }

    private static void Toggle(Action activate, Action deactivate, bool isActive)
    {
        if (isActive)
        {
            activate();
        }
        else
        {
            deactivate();
        }
    }
}

/// <summary>
/// F4 (D4): hard delete only when nothing references the entry, decided inside the delete's own transaction with
/// the row locked (<c>FOR UPDATE</c>), so a concurrent writer can't add a reference between the check and the
/// delete; an FK violation on the save is still mapped to 409. References: a category's child categories, items,
/// attributes and custom space items; an item's space items and storefront selections; an attribute's options
/// and any item/space-item value; an option's item/space-item values and storefront selections. An entry's own
/// dependants go with it: a category's space-type targets, an item's own attribute values.
/// </summary>
internal sealed class DeleteCrmCatalogEntryCommandHandler(CrmDbContext db) : ICommandHandler<DeleteCrmCatalogEntryCommand>
{
    public async Task<Result> HandleAsync(DeleteCrmCatalogEntryCommand c, CancellationToken ct = default)
    {
        IDbContextTransaction? own = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            Result result = await DeleteAsync(c, ct);
            if (own is not null)
            {
                if (result.IsSuccess)
                {
                    await own.CommitAsync(ct);
                }
                else
                {
                    await own.RollbackAsync(ct);
                }
            }

            return result;
        }
        finally
        {
            if (own is not null)
            {
                await own.DisposeAsync();
            }
        }
    }

    private async Task<Result> DeleteAsync(DeleteCrmCatalogEntryCommand c, CancellationToken ct)
    {
        Guid lockId = c.Id;
        List<Guid> locked = c.Kind switch
        {
            CrmCatalogEntryKind.Category => await db.Database
                .SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM crm.crm_catalog_categories WHERE \"Id\" = {lockId} FOR UPDATE").ToListAsync(ct),
            CrmCatalogEntryKind.Item => await db.Database
                .SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM crm.crm_catalog_items WHERE \"Id\" = {lockId} FOR UPDATE").ToListAsync(ct),
            CrmCatalogEntryKind.Attribute => await db.Database
                .SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM crm.crm_catalog_attributes WHERE \"Id\" = {lockId} FOR UPDATE").ToListAsync(ct),
            _ => await db.Database
                .SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM crm.crm_catalog_attribute_options WHERE \"Id\" = {lockId} FOR UPDATE").ToListAsync(ct),
        };
        if (locked.Count == 0)
        {
            return CrmCatalogErrors.EntryNotFound;
        }

        Guid id = c.Id;
        bool inUse = c.Kind switch
        {
            CrmCatalogEntryKind.Category =>
                await db.CrmCatalogCategories.AnyAsync(x => x.ParentId == id, ct)
                || await db.CrmCatalogItems.AnyAsync(x => x.CategoryId == id, ct)
                || await db.CrmCatalogAttributes.AnyAsync(x => x.CategoryId == id, ct)
                || await db.CrmSpaceItems.AnyAsync(x => x.CustomCategoryId == id, ct),
            CrmCatalogEntryKind.Item =>
                await db.CrmSpaceItems.AnyAsync(x => x.CatalogItemId == id, ct)
                || await db.CrmSpaceSelections.AnyAsync(x => x.CatalogItemId == id, ct),
            CrmCatalogEntryKind.Attribute =>
                await db.CrmCatalogAttributeOptions.AnyAsync(x => x.AttributeId == id, ct)
                || await db.CrmCatalogItemAttributeValues.AnyAsync(x => x.AttributeId == id, ct)
                || await db.CrmSpaceItemAttributeValues.AnyAsync(x => x.AttributeId == id, ct)
                || await db.CrmSpaceSelectionOptions.AnyAsync(x => x.AttributeId == id, ct),
            _ =>
                await db.CrmCatalogItemAttributeValues.AnyAsync(x => x.OptionId == id, ct)
                || await db.CrmSpaceItemAttributeValues.AnyAsync(x => x.OptionId == id, ct)
                || await db.CrmSpaceSelectionOptions.AnyAsync(x => x.OptionId == id, ct),
        };
        if (inUse)
        {
            return CrmCatalogErrors.InUse;
        }

        switch (c.Kind)
        {
            case CrmCatalogEntryKind.Category:
                db.CrmCatalogCategorySpaceTypes.RemoveRange(await db.CrmCatalogCategorySpaceTypes.Where(m => m.CategoryId == id).ToListAsync(ct));
                db.CrmCatalogCategories.Remove(await db.CrmCatalogCategories.SingleAsync(x => x.Id == id, ct));
                break;
            case CrmCatalogEntryKind.Item:
                db.CrmCatalogItemAttributeValues.RemoveRange(await db.CrmCatalogItemAttributeValues.Where(v => v.ItemId == id).ToListAsync(ct));
                db.CrmCatalogItems.Remove(await db.CrmCatalogItems.SingleAsync(x => x.Id == id, ct));
                break;
            case CrmCatalogEntryKind.Attribute:
                db.CrmCatalogAttributes.Remove(await db.CrmCatalogAttributes.SingleAsync(x => x.Id == id, ct));
                break;
            default:
                db.CrmCatalogAttributeOptions.Remove(await db.CrmCatalogAttributeOptions.SingleAsync(x => x.Id == id, ct));
                break;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            return CrmCatalogErrors.InUse;
        }

        return Result.Success();
    }
}

/// <summary>crm-complete FINDING-F2-1: the catalog's unique indexes are all code indexes, so a unique violation on a
/// catalog save is <see cref="CrmCatalogErrors.CodeTaken"/> (409).</summary>
internal static class CatalogWrites
{
    public static async Task<Result> SaveAsync(CrmDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return CrmCatalogErrors.CodeTaken;
        }
    }
}

internal sealed class GetCrmCatalogAttributeOptionsQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmCatalogAttributeOptionsQuery, IReadOnlyList<CrmCatalogAttributeOptionResponse>>
{
    public async Task<Result<IReadOnlyList<CrmCatalogAttributeOptionResponse>>> HandleAsync(
        GetCrmCatalogAttributeOptionsQuery query, CancellationToken ct = default)
    {
        if (!await db.CrmCatalogAttributes.AsNoTracking().AnyAsync(a => a.Id == query.AttributeId, ct))
        {
            return Result.Failure<IReadOnlyList<CrmCatalogAttributeOptionResponse>>(CrmCatalogErrors.AttributeNotFound);
        }

        List<CrmCatalogAttributeOptionResponse> options = await db.CrmCatalogAttributeOptions.AsNoTracking()
            .Where(o => o.AttributeId == query.AttributeId && (query.IncludeInactive || o.IsActive))
            .OrderBy(o => o.SortOrder).ThenBy(o => o.Code)
            .Select(o => new CrmCatalogAttributeOptionResponse(o.Id, o.AttributeId, o.Code, o.NameEn, o.NameAr, o.SortOrder, o.IsActive))
            .ToListAsync(ct);
        return options;
    }
}

internal sealed class UnmapCrmCatalogCategoryFromSpaceTypeCommandHandler(CrmDbContext db)
    : ICommandHandler<UnmapCrmCatalogCategoryFromSpaceTypeCommand>
{
    public async Task<Result> HandleAsync(UnmapCrmCatalogCategoryFromSpaceTypeCommand command, CancellationToken ct = default)
    {
        CrmCatalogCategorySpaceType? mapping = await db.CrmCatalogCategorySpaceTypes
            .FirstOrDefaultAsync(m => m.CategoryId == command.CategoryId && m.SpaceTypeId == command.SpaceTypeId, ct);
        if (mapping is null)
        {
            return CrmCatalogErrors.MappingNotFound;
        }

        db.CrmCatalogCategorySpaceTypes.Remove(mapping);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
