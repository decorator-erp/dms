using BuildingBlocks.Messaging;
using Crm.Contracts.Catalog;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Catalog;

internal sealed class CreateCrmCatalogAttributeCommandValidator : AbstractValidator<CreateCrmCatalogAttributeCommand>
{
    public CreateCrmCatalogAttributeCommandValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(128);
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(128);
    }
}

internal sealed class CreateCrmCatalogAttributeCommandHandler(CrmDbContext db)
    : ICommandHandler<CreateCrmCatalogAttributeCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateCrmCatalogAttributeCommand command, CancellationToken ct = default)
    {
        bool categoryExists = await db.CrmCatalogCategories.AnyAsync(c => c.Id == command.CategoryId, ct);
        if (!categoryExists)
        {
            return Result.Failure<Guid>(CrmCatalogErrors.CategoryNotFound);
        }

        Result<CrmCatalogAttribute> result = CrmCatalogAttribute.Create(
            command.CategoryId, command.Code, command.NameEn, command.NameAr,
            CrmEnumMapping.ToDomain(command.DataType), command.Unit, command.IsRequired, command.SortOrder);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        CrmCatalogAttribute attribute = result.Value!;
        db.CrmCatalogAttributes.Add(attribute);
        // crm-complete FINDING-F2-1: a taken code is 409, not a 500.
        Result saved = await CatalogWrites.SaveAsync(db, ct);
        if (saved.IsFailure)
        {
            return Result.Failure<Guid>(saved.Error);
        }


        return attribute.Id;
    }
}
