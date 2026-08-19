using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Finance.Contracts;
using Finance.Contracts.CostCategories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Finance.Infrastructure.Endpoints;

/// <summary>
/// Global cost-classification master data — six routes, no scope, no owner.
///
/// <para><b>Reading is <c>finance.read</c>; every write is <c>finance.manage_cost_category</c>.</b> The
/// split matters because these rows are referenced by every budget line in the system: a user who can
/// file spending against a category should not necessarily be able to invent or withdraw one.</para>
/// </summary>
internal sealed class CostCategoryEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/cost-categories").WithTags("CostCategories");

        group.MapPost("/", async (
            CreateCostCategoryRequest request,
            ICommandHandler<CreateCostCategoryCommand, Guid> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new CreateCostCategoryCommand(request), ct);

            return result.Match(
                id => Results.Created($"/api/cost-categories/{id}", new { CostCategoryId = id }),
                r => r.ToProblem());
        }).RequirePermission(FinancePermissions.ManageCostCategory);

        group.MapGet("/", async (
            // Nullable on purpose: a non-nullable query parameter is REQUIRED in minimal APIs, and
            // paging must have working defaults.
            int? skip, int? take, string? search, bool? isActive,
            IQueryHandler<GetCostCategoryListQuery, PagedResult<CostCategoryListItemResponse>> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new GetCostCategoryListQuery(skip ?? 0, take ?? 50, search, isActive), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        group.MapGet("/{id:guid}", async (
            Guid id,
            IQueryHandler<GetCostCategoryByIdQuery, CostCategoryResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetCostCategoryByIdQuery(id), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        group.MapPut("/{id:guid}", async (
            Guid id, UpdateCostCategoryRequest request,
            ICommandHandler<UpdateCostCategoryCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new UpdateCostCategoryCommand(id, request), ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.ManageCostCategory);

        group.MapPut("/{id:guid}/activate", async (
            Guid id, ICommandHandler<ActivateCostCategoryCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new ActivateCostCategoryCommand(id), ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.ManageCostCategory);

        group.MapPut("/{id:guid}/deactivate", async (
            Guid id, ICommandHandler<DeactivateCostCategoryCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new DeactivateCostCategoryCommand(id), ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.ManageCostCategory);
    }
}
