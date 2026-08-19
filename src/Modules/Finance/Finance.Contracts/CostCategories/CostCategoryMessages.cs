using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;

namespace Finance.Contracts.CostCategories;

// ── Requests ───────────────────────────────────────────────────────────────────

public sealed record CreateCostCategoryRequest(string Code, string Name, string? Description = null);

public sealed record UpdateCostCategoryRequest(string Name, string? Description = null);

// ── Responses ──────────────────────────────────────────────────────────────────

public sealed record CostCategoryResponse(
    Guid Id, string Code, string Name, string? Description, bool IsActive,
    DateTime CreatedAtUtc, DateTime? UpdatedAtUtc);

public sealed record CostCategoryListItemResponse(Guid Id, string Code, string Name, bool IsActive);

// ── Commands ───────────────────────────────────────────────────────────────────

public sealed record CreateCostCategoryCommand(CreateCostCategoryRequest Request) : ICommand<Guid>;

public sealed record UpdateCostCategoryCommand(Guid Id, UpdateCostCategoryRequest Request) : ICommand;

public sealed record ActivateCostCategoryCommand(Guid Id) : ICommand;

public sealed record DeactivateCostCategoryCommand(Guid Id) : ICommand;

// ── Queries ────────────────────────────────────────────────────────────────────

public sealed record GetCostCategoryByIdQuery(Guid Id) : IQuery<CostCategoryResponse>;

public sealed record GetCostCategoryListQuery(
    int Skip = 0,
    int Take = 50,
    string? Search = null,
    bool? IsActive = null) : IQuery<PagedResult<CostCategoryListItemResponse>>;
