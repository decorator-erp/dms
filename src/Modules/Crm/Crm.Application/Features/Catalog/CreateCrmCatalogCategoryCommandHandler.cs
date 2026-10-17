using BuildingBlocks.Messaging;
using Crm.Contracts.Catalog;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Catalog;

internal sealed class CreateCrmCatalogCategoryCommandValidator : AbstractValidator<CreateCrmCatalogCategoryCommand>
{
    public CreateCrmCatalogCategoryCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.NameEn).NotEmpty();
        RuleFor(x => x.NameAr).NotEmpty();
    }
}

/// <summary>
/// Writes <see cref="CrmCatalogCategory.Path"/>/<see cref="CrmCatalogCategory.Depth"/> at creation —
/// the Application handler that owns the "derived state" write the entity's own remarks reserve for
/// it. Path format: <c>/{ancestor}/…/{own id}/</c>, root = <c>/{own id}/</c> at depth 0.
/// </summary>
internal sealed class CreateCrmCatalogCategoryCommandHandler(CrmDbContext db)
    : ICommandHandler<CreateCrmCatalogCategoryCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateCrmCatalogCategoryCommand command, CancellationToken ct = default)
    {
        CrmCatalogCategory? parent = null;
        if (command.ParentId is { } parentId)
        {
            parent = await db.CrmCatalogCategories.FirstOrDefaultAsync(c => c.Id == parentId, ct);
            if (parent is null)
            {
                return Result.Failure<Guid>(CrmCatalogErrors.CategoryNotFound);
            }
        }

        int depth = (parent?.Depth ?? -1) + 1;
        if (depth > CrmCatalogCategory.MaxDepth)
        {
            return Result.Failure<Guid>(CrmCatalogErrors.MaxDepthExceeded);
        }

        Result<CrmCatalogCategory> categoryResult = CrmCatalogCategory.Create(
            command.ParentId, command.Code, command.NameEn, command.NameAr,
            CrmEnumMapping.ToDomain(command.SelectionMode), command.IsRequired, command.SortOrder,
            command.DescriptionEn, command.DescriptionAr, command.ImageUri);
        if (categoryResult.IsFailure)
        {
            return Result.Failure<Guid>(categoryResult.Error);
        }

        CrmCatalogCategory category = categoryResult.Value!;
        category.SetPath(CategoryPathHelper.ChildPath(parent?.Path, category.Id), depth);

        db.CrmCatalogCategories.Add(category);
        await db.SaveChangesAsync(ct);

        return category.Id;
    }
}
