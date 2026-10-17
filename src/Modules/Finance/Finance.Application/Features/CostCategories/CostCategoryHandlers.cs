using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Finance.Contracts.CostCategories;
using Finance.Domain;
using Finance.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Finance.Application.Features.CostCategories;

// Global reference data: no project, no scope, no owner. The simplest surface in the module, and the
// only one with no money column anywhere in it.

internal sealed class CreateCostCategoryCommandValidator : AbstractValidator<CreateCostCategoryCommand>
{
    public CreateCostCategoryCommandValidator()
    {
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Request.Description).MaximumLength(512);
    }
}

/// <summary>
/// DIVERGENCE from the backend's validator, which caps Code at 50, Name at 200 and Description at 1000
/// while its own EF configuration declares 32, 128 and 512. The validator is looser than the column in
/// all three cases, so a 40-character code passes validation and then fails as a truncation error from
/// Postgres. The lengths here match the columns, which is the only pair of numbers that can agree.
/// </summary>
internal sealed class CreateCostCategoryCommandHandler(FinanceDbContext db)
    : ICommandHandler<CreateCostCategoryCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateCostCategoryCommand command, CancellationToken ct = default)
    {
        // Exact-match, deliberately: the unique index is a plain index on "Code", not a functional one
        // over lower("Code"), so a case-insensitive check here would reject codes the database is
        // willing to store and the two would disagree about what "duplicate" means.
        if (await db.CostCategories.AnyAsync(c => c.Code == command.Request.Code, ct))
        {
            return CostCategoryErrors.DuplicatedCode;
        }

        Result<CostCategory> category = CostCategory.Create(
            command.Request.Code, command.Request.Name, command.Request.Description);

        if (category.IsFailure)
        {
            return category.Error;
        }

        db.CostCategories.Add(category.Value!);
        await db.SaveChangesAsync(ct);

        return category.Value!.Id;
    }
}

internal sealed class UpdateCostCategoryCommandValidator : AbstractValidator<UpdateCostCategoryCommand>
{
    public UpdateCostCategoryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Request.Description).MaximumLength(512);
    }
}

/// <summary>Only an ACTIVE category can be edited — the backend's <c>ExecuteUpdate</c> predicate is
/// <c>c.Id == id &amp;&amp; c.IsActive</c>, so editing a deactivated one reports NotFound. Ported
/// as-is: a deactivated category is withdrawn from use, and reactivating it is the explicit step.</summary>
internal sealed class UpdateCostCategoryCommandHandler(FinanceDbContext db)
    : ICommandHandler<UpdateCostCategoryCommand>
{
    public async Task<Result> HandleAsync(UpdateCostCategoryCommand command, CancellationToken ct = default)
    {
        CostCategory? category = await db.CostCategories
            .FirstOrDefaultAsync(c => c.Id == command.Id && c.IsActive, ct);

        if (category is null)
        {
            return CostCategoryErrors.NotFound;
        }

        Result updated = category.Update(command.Request.Name, command.Request.Description);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// <summary>Activate and deactivate do NOT filter on <c>IsActive</c> — that is the whole point, and it
/// makes both idempotent rather than one of them being a no-op that reports NotFound.</summary>
internal sealed class ActivateCostCategoryCommandHandler(FinanceDbContext db)
    : ICommandHandler<ActivateCostCategoryCommand>
{
    public async Task<Result> HandleAsync(ActivateCostCategoryCommand command, CancellationToken ct = default)
    {
        CostCategory? category = await db.CostCategories.FirstOrDefaultAsync(c => c.Id == command.Id, ct);
        if (category is null)
        {
            return CostCategoryErrors.NotFound;
        }

        category.Activate();
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// <summary>
/// Deactivating does NOT check for budget lines already filed under the category, matching the backend.
/// It cannot orphan anything: <c>budget_lines."CostCategoryId"</c> is <c>ON DELETE SET NULL</c> and this
/// is not a delete at all — the row stays, the classification stays, and existing budget lines keep
/// pointing at it. What deactivation prevents is <c>CreateBudgetLineCommandHandler</c> accepting it for
/// NEW lines, which is exactly the intended effect.
/// </summary>
internal sealed class DeactivateCostCategoryCommandHandler(FinanceDbContext db)
    : ICommandHandler<DeactivateCostCategoryCommand>
{
    public async Task<Result> HandleAsync(DeactivateCostCategoryCommand command, CancellationToken ct = default)
    {
        CostCategory? category = await db.CostCategories.FirstOrDefaultAsync(c => c.Id == command.Id, ct);
        if (category is null)
        {
            return CostCategoryErrors.NotFound;
        }

        category.Deactivate();
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class GetCostCategoryByIdQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetCostCategoryByIdQuery, CostCategoryResponse>
{
    public async Task<Result<CostCategoryResponse>> HandleAsync(
        GetCostCategoryByIdQuery query, CancellationToken ct = default)
    {
        // No IsActive filter, matching the backend: a deactivated category must still be readable, or
        // every budget line pointing at one becomes unexplainable in the UI.
        CostCategoryResponse? response = await db.CostCategories
            .Where(c => c.Id == query.Id)
            .Select(c => new CostCategoryResponse(
                c.Id, c.Code, c.Name, c.Description, c.IsActive, c.CreatedAtUtc, c.UpdatedAtUtc))
            .FirstOrDefaultAsync(ct);

        return response is null ? CostCategoryErrors.NotFound : response;
    }
}

internal sealed class GetCostCategoryListQueryValidator : AbstractValidator<GetCostCategoryListQuery>
{
    public GetCostCategoryListQueryValidator()
    {
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 200);
    }
}

internal sealed class GetCostCategoryListQueryHandler(FinanceDbContext db)
    : IQueryHandler<GetCostCategoryListQuery, PagedResult<CostCategoryListItemResponse>>
{
    public async Task<Result<PagedResult<CostCategoryListItemResponse>>> HandleAsync(
        GetCostCategoryListQuery query, CancellationToken ct = default)
    {
        IQueryable<CostCategory> q = db.CostCategories.AsQueryable();

        if (query.IsActive.HasValue)
        {
            q = q.Where(c => c.IsActive == query.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string search = query.Search.Trim();
            q = q.Where(c => c.Code.Contains(search) || c.Name.Contains(search));
        }

        int total = await q.CountAsync(ct);

        List<CostCategoryListItemResponse> items = await q
            .OrderBy(c => c.Code)
            .Skip(query.Skip)
            .Take(query.Take)
            .Select(c => new CostCategoryListItemResponse(c.Id, c.Code, c.Name, c.IsActive))
            .ToListAsync(ct);

        return new PagedResult<CostCategoryListItemResponse>(
            items, query.Skip / Math.Max(query.Take, 1), query.Take, total);
    }
}
