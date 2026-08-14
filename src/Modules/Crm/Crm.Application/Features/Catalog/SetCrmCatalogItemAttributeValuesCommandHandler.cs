using BuildingBlocks.Messaging;
using Crm.Contracts.Catalog;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Catalog;

internal sealed class SetCrmCatalogItemAttributeValuesCommandValidator : AbstractValidator<SetCrmCatalogItemAttributeValuesCommand>
{
    public SetCrmCatalogItemAttributeValuesCommandValidator() => RuleFor(x => x.ItemId).NotEmpty();
}

/// <summary>Corrections are replace-all per item, matching <c>CrmCatalogItemAttributeValue</c>'s own
/// "no update method" rule — every existing value row for this item is removed and the request's own
/// list is created fresh, in the same transaction.</summary>
internal sealed class SetCrmCatalogItemAttributeValuesCommandHandler(CrmDbContext db)
    : ICommandHandler<SetCrmCatalogItemAttributeValuesCommand>
{
    public async Task<Result> HandleAsync(SetCrmCatalogItemAttributeValuesCommand command, CancellationToken ct = default)
    {
        var itemExists = await db.CrmCatalogItems.AnyAsync(i => i.Id == command.ItemId, ct);
        if (!itemExists)
            return CrmCatalogErrors.ItemNotFound;

        var attributeIds = command.Values.Select(v => v.AttributeId).ToList();
        var attributes = await db.CrmCatalogAttributes
            .Where(a => attributeIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);

        var created = new List<CrmCatalogItemAttributeValue>();
        foreach (var value in command.Values)
        {
            if (!attributes.TryGetValue(value.AttributeId, out var attribute))
                return CrmCatalogErrors.AttributeNotFound;

            var result = CrmCatalogItemAttributeValue.Create(
                command.ItemId, value.AttributeId, attribute.DataType, value.ValueText, value.ValueNumber,
                value.ValueBool, value.ValueDate, value.OptionId);
            if (result.IsFailure)
                return result.Error;

            created.Add(result.Value!);
        }

        var existing = await db.CrmCatalogItemAttributeValues.Where(v => v.ItemId == command.ItemId).ToListAsync(ct);
        db.CrmCatalogItemAttributeValues.RemoveRange(existing);
        db.CrmCatalogItemAttributeValues.AddRange(created);

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
