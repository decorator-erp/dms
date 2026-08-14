using BuildingBlocks.Messaging;
using Crm.Contracts.Catalog;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Catalog;

internal sealed class MoveCrmCatalogCategoryCommandValidator : AbstractValidator<MoveCrmCatalogCategoryCommand>
{
    public MoveCrmCatalogCategoryCommandValidator() => RuleFor(x => x.CategoryId).NotEmpty();
}

/// <summary>
/// Reparents a category and rewrites its whole subtree's <c>Path</c>/<c>Depth</c> in one transaction —
/// the "subtree rewrite" <c>CrmCatalogCategory</c>'s own remarks reserve for the Application handler
/// that owns a move. Loaded in memory (the catalog is "tens of rows, not thousands" per the entity's
/// own doc comment, so a bulk `ExecuteUpdate` isn't needed for this port's scale) rather than a raw-SQL
/// prefix rewrite.
/// </summary>
internal sealed class MoveCrmCatalogCategoryCommandHandler(CrmDbContext db)
    : ICommandHandler<MoveCrmCatalogCategoryCommand>
{
    public async Task<Result> HandleAsync(MoveCrmCatalogCategoryCommand command, CancellationToken ct = default)
    {
        var target = await db.CrmCatalogCategories.FirstOrDefaultAsync(c => c.Id == command.CategoryId, ct);
        if (target is null)
            return CrmCatalogErrors.CategoryNotFound;

        CrmCatalogCategory? newParent = null;
        if (command.NewParentId is { } newParentId)
        {
            if (newParentId == target.Id)
                return CrmCatalogErrors.CyclicMove;

            newParent = await db.CrmCatalogCategories.FirstOrDefaultAsync(c => c.Id == newParentId, ct);
            if (newParent is null)
                return CrmCatalogErrors.CategoryNotFound;

            // A move under one of the target's own descendants is a cycle - the new parent's path
            // would start with the target's own path.
            if (newParent.Path.StartsWith(target.Path, StringComparison.Ordinal))
                return CrmCatalogErrors.CyclicMove;
        }

        var oldPrefix = target.Path;
        var newDepth = (newParent?.Depth ?? -1) + 1;
        var depthDelta = newDepth - target.Depth;
        var newPrefix = CategoryPathHelper.ChildPath(newParent?.Path, target.Id);

        var subtree = await db.CrmCatalogCategories
            .Where(c => c.Path.StartsWith(oldPrefix))
            .ToListAsync(ct);

        if (subtree.Count > 0 && subtree.Max(c => c.Depth) + depthDelta > CrmCatalogCategory.MaxDepth)
            return CrmCatalogErrors.MaxDepthExceeded;

        foreach (var descendant in subtree)
        {
            var rebasedPath = CategoryPathHelper.Rebase(descendant.Path, oldPrefix, newPrefix);
            descendant.SetPath(rebasedPath, descendant.Depth + depthDelta);
        }

        target.SetParent(command.NewParentId);

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
