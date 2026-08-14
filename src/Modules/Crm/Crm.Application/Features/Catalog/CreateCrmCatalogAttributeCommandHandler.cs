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
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.NameEn).NotEmpty();
        RuleFor(x => x.NameAr).NotEmpty();
    }
}

internal sealed class CreateCrmCatalogAttributeCommandHandler(CrmDbContext db)
    : ICommandHandler<CreateCrmCatalogAttributeCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateCrmCatalogAttributeCommand command, CancellationToken ct = default)
    {
        var categoryExists = await db.CrmCatalogCategories.AnyAsync(c => c.Id == command.CategoryId, ct);
        if (!categoryExists)
            return Result.Failure<Guid>(CrmCatalogErrors.CategoryNotFound);

        var result = CrmCatalogAttribute.Create(
            command.CategoryId, command.Code, command.NameEn, command.NameAr,
            CrmEnumMapping.ToDomain(command.DataType), command.Unit, command.IsRequired, command.SortOrder);
        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        var attribute = result.Value!;
        db.CrmCatalogAttributes.Add(attribute);
        await db.SaveChangesAsync(ct);

        return attribute.Id;
    }
}
