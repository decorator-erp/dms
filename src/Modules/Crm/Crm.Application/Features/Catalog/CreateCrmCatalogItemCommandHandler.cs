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
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.NameEn).NotEmpty();
        RuleFor(x => x.NameAr).NotEmpty();
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
        await db.SaveChangesAsync(ct);

        return item.Id;
    }
}
