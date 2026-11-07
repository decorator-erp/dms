using BuildingBlocks.Messaging;
using Crm.Contracts.Catalog;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Catalog;

internal sealed class CreateCrmCatalogAttributeOptionCommandValidator : AbstractValidator<CreateCrmCatalogAttributeOptionCommand>
{
    public CreateCrmCatalogAttributeOptionCommandValidator()
    {
        RuleFor(x => x.AttributeId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(128);
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(128);
    }
}

internal sealed class CreateCrmCatalogAttributeOptionCommandHandler(CrmDbContext db)
    : ICommandHandler<CreateCrmCatalogAttributeOptionCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateCrmCatalogAttributeOptionCommand command, CancellationToken ct = default)
    {
        bool attributeExists = await db.CrmCatalogAttributes.AnyAsync(a => a.Id == command.AttributeId, ct);
        if (!attributeExists)
        {
            return Result.Failure<Guid>(CrmCatalogErrors.AttributeNotFound);
        }

        Result<CrmCatalogAttributeOption> result = CrmCatalogAttributeOption.Create(
            command.AttributeId, command.Code, command.NameEn, command.NameAr, command.ImageUri, command.SortOrder);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        CrmCatalogAttributeOption option = result.Value!;
        db.CrmCatalogAttributeOptions.Add(option);
        // crm-complete FINDING-F2-1: a taken code is 409, not a 500.
        Result saved = await CatalogWrites.SaveAsync(db, ct);
        if (saved.IsFailure)
        {
            return Result.Failure<Guid>(saved.Error);
        }


        return option.Id;
    }
}
