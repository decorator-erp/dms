using System.Text.Json;
using BuildingBlocks.Messaging;
using Crm.Contracts.Spaces;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Spaces;

/// <summary>crm-complete F6: reads picks with catalog names resolved (a fixed number of statements for any number
/// of spaces) and (de)serialises the brief-space snapshot.</summary>
internal static class StorefrontReads
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<Dictionary<Guid, List<CrmSpaceSelectionResponse>>> LoadAsync(
        CrmDbContext db, IReadOnlyCollection<Guid> spaceIds, CancellationToken ct)
    {
        List<CrmSpaceSelection> selections = await db.CrmSpaceSelections.AsNoTracking()
            .Include(s => s.Options)
            .Where(s => spaceIds.Contains(s.SpaceId))
            .OrderBy(s => s.SortOrder).ThenBy(s => s.CreatedAtUtc)
            .ToListAsync(ct);
        List<Guid> itemIds = [.. selections.Select(s => s.CatalogItemId).Distinct()];
        List<Guid> optionIds = [.. selections.SelectMany(s => s.Options).Select(o => o.OptionId).Distinct()];
        List<Guid> attributeIds = [.. selections.SelectMany(s => s.Options).Select(o => o.AttributeId).Distinct()];
        Dictionary<Guid, CrmCatalogItem> items = await db.CrmCatalogItems.AsNoTracking().Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct);
        Dictionary<Guid, CrmCatalogAttributeOption> options = await db.CrmCatalogAttributeOptions.AsNoTracking().Where(o => optionIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, ct);
        Dictionary<Guid, CrmCatalogAttribute> attributes = await db.CrmCatalogAttributes.AsNoTracking().Where(a => attributeIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);

        return selections
            .GroupBy(s => s.SpaceId)
            .ToDictionary(g => g.Key, g => g.Select(s => new CrmSpaceSelectionResponse(
                s.Id, s.SpaceId, s.CatalogItemId, items[s.CatalogItemId].Code, items[s.CatalogItemId].NameEn,
                items[s.CatalogItemId].NameAr, s.Quantity, s.Notes, s.SortOrder,
                [.. s.Options.OrderBy(o => attributes[o.AttributeId].SortOrder).Select(o => new CrmSpaceSelectionOptionResponse(
                    o.AttributeId, attributes[o.AttributeId].NameEn, attributes[o.AttributeId].NameAr, o.OptionId,
                    options[o.OptionId].Code, options[o.OptionId].NameEn, options[o.OptionId].NameAr))])).ToList());
    }

    public static string Serialize(IReadOnlyList<CrmSpaceSelectionResponse> selections) => JsonSerializer.Serialize(selections, Json);

    public static IReadOnlyList<CrmSpaceSelectionResponse> Parse(string? json) =>
        string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<CrmSpaceSelectionResponse>>(json, Json) ?? [];

    /// <summary>D3 rules: the item exists and is active; its category is targeted to the space's type (directly or
    /// through an ancestor); each option is an active option of the named attribute, the attribute belongs to the
    /// item's category or an ancestor, one option per attribute.</summary>
    public static async Task<Result> ValidateAsync(
        CrmDbContext db, Guid spaceId, Guid catalogItemId, IReadOnlyList<SpaceSelectionOptionInput>? chosen, CancellationToken ct)
    {
        Guid spaceTypeId = await db.CrmSpaces.AsNoTracking().Where(s => s.Id == spaceId).Select(s => s.SpaceTypeId).SingleAsync(ct);
        var item = await db.CrmCatalogItems.AsNoTracking()
            .Where(i => i.Id == catalogItemId && i.IsActive)
            .Join(db.CrmCatalogCategories, i => i.CategoryId, c => c.Id, (i, c) => new { c.Path })
            .FirstOrDefaultAsync(ct);
        if (item is null)
        {
            return CrmStorefrontErrors.ItemNotAvailable;
        }

        // FINDING-F-2: every category on the item's path must be active, and only active mapped categories target.
        List<Guid> pathIds = [.. item.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse)];
        if (await db.CrmCatalogCategories.AsNoTracking().AnyAsync(c => pathIds.Contains(c.Id) && !c.IsActive, ct))
        {
            return CrmStorefrontErrors.ItemNotAvailable;
        }

        List<string> targeted = await db.CrmCatalogCategorySpaceTypes.AsNoTracking()
            .Where(m => m.SpaceTypeId == spaceTypeId)
            .Join(db.CrmCatalogCategories.Where(c => c.IsActive), m => m.CategoryId, c => c.Id, (m, c) => c.Path)
            .ToListAsync(ct);
        if (!targeted.Exists(p => item.Path.StartsWith(p, StringComparison.Ordinal)))
        {
            return CrmStorefrontErrors.CategoryNotTargeted;
        }

        if (chosen is null || chosen.Count == 0)
        {
            return Result.Success();
        }

        if (chosen.Select(o => o.AttributeId).Distinct().Count() != chosen.Count)
        {
            return CrmStorefrontErrors.OptionNotOfAttribute;
        }

        List<Guid> optionIds = [.. chosen.Select(o => o.OptionId)];
        var found = await db.CrmCatalogAttributeOptions.AsNoTracking()
            .Where(o => optionIds.Contains(o.Id) && o.IsActive)
            .Join(db.CrmCatalogAttributes.Where(a => a.IsActive), o => o.AttributeId, a => a.Id, (o, a) => new { o.Id, o.AttributeId, a.CategoryId })
            .ToListAsync(ct);
        bool valid = chosen.All(c => found.Exists(f =>
            f.Id == c.OptionId && f.AttributeId == c.AttributeId && item.Path.Contains($"/{f.CategoryId}/", StringComparison.Ordinal)));
        return valid ? Result.Success() : CrmStorefrontErrors.OptionNotOfAttribute;
    }
}

internal sealed class AddCrmSpaceSelectionCommandValidator : AbstractValidator<AddCrmSpaceSelectionCommand>
{
    public AddCrmSpaceSelectionCommandValidator()
    {
        RuleFor(x => x.CatalogItemId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(0.001m).LessThanOrEqualTo(1_000_000).PrecisionScale(18, 3, true); // FINDING-F-7
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Options).Must(o => o.Count <= 50);
    }
}

internal sealed class UpdateCrmSpaceSelectionCommandValidator : AbstractValidator<UpdateCrmSpaceSelectionCommand>
{
    public UpdateCrmSpaceSelectionCommandValidator()
    {
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(0.001m).LessThanOrEqualTo(1_000_000).PrecisionScale(18, 3, true); // FINDING-F-7
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Options).Must(o => o is null || o.Count <= 50);
    }
}

internal sealed class GetCrmSpaceSelectionsQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmSpaceSelectionsQuery, IReadOnlyList<CrmSpaceSelectionResponse>>
{
    public async Task<Result<IReadOnlyList<CrmSpaceSelectionResponse>>> HandleAsync(GetCrmSpaceSelectionsQuery query, CancellationToken ct = default) =>
        (await StorefrontReads.LoadAsync(db, [query.SpaceId], ct)).GetValueOrDefault(query.SpaceId) ?? [];
}

/// <summary>F6 (D3): office only (<c>crm.spaces.manage</c>) behind the CRM project scope (404).</summary>
internal sealed class AddCrmSpaceSelectionCommandHandler(CrmDbContext db) : ICommandHandler<AddCrmSpaceSelectionCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(AddCrmSpaceSelectionCommand command, CancellationToken ct = default)
    {
        Result valid = await StorefrontReads.ValidateAsync(db, command.SpaceId, command.CatalogItemId, command.Options, ct);
        if (valid.IsFailure)
        {
            return Result.Failure<Guid>(valid.Error);
        }

        int next = (await db.CrmSpaceSelections.Where(s => s.SpaceId == command.SpaceId).MaxAsync(s => (int?)s.SortOrder, ct) ?? 0) + 1;
        var selection = CrmSpaceSelection.Create(command.SpaceId, command.CatalogItemId, command.Quantity, command.Notes?.Trim(), next);
        selection.ReplaceOptions(command.Options.Select(o => (o.AttributeId, o.OptionId)));
        db.CrmSpaceSelections.Add(selection);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.ForeignKeyViolation })
        {
            return Result.Failure<Guid>(CrmStorefrontErrors.ItemNotAvailable); // FINDING-F-8: the item or space went meanwhile
        }

        return selection.Id;
    }
}

internal sealed class UpdateCrmSpaceSelectionCommandHandler(CrmDbContext db) : ICommandHandler<UpdateCrmSpaceSelectionCommand>
{
    public async Task<Result> HandleAsync(UpdateCrmSpaceSelectionCommand command, CancellationToken ct = default)
    {
        CrmSpaceSelection? selection = await db.CrmSpaceSelections.Include(s => s.Options)
            .FirstOrDefaultAsync(s => s.Id == command.SelectionId && s.SpaceId == command.SpaceId, ct);
        if (selection is null)
        {
            return CrmStorefrontErrors.SelectionNotFound;
        }

        if (command.Options is not null)
        {
            Result valid = await StorefrontReads.ValidateAsync(db, command.SpaceId, selection.CatalogItemId, command.Options, ct);
            if (valid.IsFailure)
            {
                return valid;
            }

            db.CrmSpaceSelectionOptions.RemoveRange(selection.Options);
            selection.ReplaceOptions(command.Options.Select(o => (o.AttributeId, o.OptionId)));
        }

        selection.Update(command.Quantity, command.Notes?.Trim());
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return CrmStorefrontErrors.ConcurrentUpdate;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.ForeignKeyViolation })
        {
            return CrmStorefrontErrors.OptionNotOfAttribute; // FINDING-F2-2: the option went meanwhile
        }

        return Result.Success();
    }
}

internal sealed class RemoveCrmSpaceSelectionCommandHandler(CrmDbContext db) : ICommandHandler<RemoveCrmSpaceSelectionCommand>
{
    public async Task<Result> HandleAsync(RemoveCrmSpaceSelectionCommand command, CancellationToken ct = default)
    {
        CrmSpaceSelection? selection = await db.CrmSpaceSelections
            .FirstOrDefaultAsync(s => s.Id == command.SelectionId && s.SpaceId == command.SpaceId, ct);
        if (selection is null)
        {
            return CrmStorefrontErrors.SelectionNotFound;
        }

        db.CrmSpaceSelections.Remove(selection);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return CrmStorefrontErrors.SelectionNotFound;
        }

        return Result.Success();
    }
}

internal sealed class ReorderCrmSpaceSelectionsCommandHandler(CrmDbContext db) : ICommandHandler<ReorderCrmSpaceSelectionsCommand>
{
    public async Task<Result> HandleAsync(ReorderCrmSpaceSelectionsCommand command, CancellationToken ct = default)
    {
        List<CrmSpaceSelection> selections = await db.CrmSpaceSelections.Where(s => s.SpaceId == command.SpaceId).ToListAsync(ct);
        if (command.SelectionIds.Distinct().Count() != command.SelectionIds.Count
            || selections.Count != command.SelectionIds.Count
            || !selections.Select(s => s.Id).ToHashSet().SetEquals(command.SelectionIds))
        {
            return CrmStorefrontErrors.InvalidOrder;
        }

        var byId = selections.ToDictionary(s => s.Id);
        for (int i = 0; i < command.SelectionIds.Count; i++)
        {
            byId[command.SelectionIds[i]].SetSortOrder(i + 1);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return CrmStorefrontErrors.ConcurrentUpdate;
        }

        return Result.Success();
    }
}
