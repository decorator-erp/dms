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
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.NameEn).NotEmpty();
        RuleFor(x => x.NameAr).NotEmpty();
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
        await db.SaveChangesAsync(ct);

        return option.Id;
    }
}
