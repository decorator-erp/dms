using BuildingBlocks.Messaging;
using Crm.Contracts.Catalog;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Catalog;

internal sealed class CreateCrmCatalogItemCommandValidator : AbstractValidator<CreateCrmCatalogItemCommand>
{
    public CreateCrmCatalogItemCommandValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(256);
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(256);
    }
}

internal sealed class CreateCrmCatalogItemCommandHandler(CrmDbContext db) : ICommandHandler<CreateCrmCatalogItemCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateCrmCatalogItemCommand command, CancellationToken ct = default)
    {
        bool categoryExists = await db.CrmCatalogCategories.AnyAsync(c => c.Id == command.CategoryId, ct);
        if (!categoryExists)
        {
            return Result.Failure<Guid>(CrmCatalogErrors.CategoryNotFound);
        }

        Result<CrmCatalogItem> result = CrmCatalogItem.Create(
            command.CategoryId, command.Code, command.NameEn, command.NameAr, command.IsPurchasable, command.SortOrder);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        CrmCatalogItem item = result.Value!;
        db.CrmCatalogItems.Add(item);
        // crm-complete FINDING-F2-1: a taken code is 409, not a 500.
        Result saved = await CatalogWrites.SaveAsync(db, ct);
        if (saved.IsFailure)
        {
            return Result.Failure<Guid>(saved.Error);
        }


        return item.Id;
    }
}
