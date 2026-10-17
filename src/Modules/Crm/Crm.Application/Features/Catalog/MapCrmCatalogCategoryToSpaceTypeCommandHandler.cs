using BuildingBlocks.Messaging;
using Crm.Contracts.Catalog;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Catalog;

internal sealed class MapCrmCatalogCategoryToSpaceTypeCommandValidator : AbstractValidator<MapCrmCatalogCategoryToSpaceTypeCommand>
{
    public MapCrmCatalogCategoryToSpaceTypeCommandValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.SpaceTypeId).NotEmpty();
    }
}

/// <summary>Cascades to the category's whole subtree and their items on the read side (G9-11's
/// query handlers) — this write only records the one mapping row.</summary>
internal sealed class MapCrmCatalogCategoryToSpaceTypeCommandHandler(CrmDbContext db)
    : ICommandHandler<MapCrmCatalogCategoryToSpaceTypeCommand>
{
    public async Task<Result> HandleAsync(MapCrmCatalogCategoryToSpaceTypeCommand command, CancellationToken ct = default)
    {
        bool alreadyMapped = await db.CrmCatalogCategorySpaceTypes.AnyAsync(
            m => m.CategoryId == command.CategoryId && m.SpaceTypeId == command.SpaceTypeId, ct);
        if (alreadyMapped)
        {
            return CrmCatalogErrors.DuplicateMapping;
        }

        Result<CrmCatalogCategorySpaceType> result = CrmCatalogCategorySpaceType.Create(command.CategoryId, command.SpaceTypeId);
        if (result.IsFailure)
        {
            return result.Error;
        }

        db.CrmCatalogCategorySpaceTypes.Add(result.Value!);
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
