using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Finance.Contracts;
using Finance.Contracts.BudgetLines;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Finance.Infrastructure.Endpoints;

/// <summary>
/// Budget lines — five routes. Writes are <c>finance.manage_budget</c>, reads <c>finance.read</c>.
///
/// <para><b><c>recalculate</c> is a write, not a read, and is permissioned as one.</b> It re-derives the
/// committed and actual aggregates from a full ledger re-sum and overwrites them, so a caller who could
/// run it with only <c>finance.read</c> could silently rewrite the numbers a project is managed
/// by.</para>
/// </summary>
internal sealed class BudgetLineEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/budget-lines").WithTags("BudgetLines");

        group.MapPost("/", async (
            CreateBudgetLineRequest request,
            ICommandHandler<CreateBudgetLineCommand, Guid> handler,
            CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(new CreateBudgetLineCommand(request), ct);

            return result.Match(
                id => Results.Created($"/api/budget-lines/{id}", new { BudgetLineId = id }),
                r => r.ToProblem());
        }).RequirePermission(FinancePermissions.ManageBudget);

        group.MapGet("/{id:guid}", async (
            Guid id, IQueryHandler<GetBudgetLineByIdQuery, BudgetLineResponse> handler,
            CancellationToken ct) =>
        {
            Result<BudgetLineResponse> result = await handler.HandleAsync(new GetBudgetLineByIdQuery(id), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        group.MapGet("/by-project/{projectId:guid}", async (
            Guid projectId, Guid? costCategoryId, Guid? projectStageId, Guid? projectTaskId,
            IQueryHandler<GetBudgetLinesByProjectQuery, IReadOnlyList<BudgetLineListItemResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<BudgetLineListItemResponse>> result = await handler.HandleAsync(
                new GetBudgetLinesByProjectQuery(projectId, costCategoryId, projectStageId, projectTaskId), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        group.MapPut("/{id:guid}/allocation", async (
            Guid id, UpdateBudgetLineAllocationRequest request,
            ICommandHandler<UpdateBudgetLineAllocationCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new UpdateBudgetLineAllocationCommand(id, request.AllocatedAmount), ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.ManageBudget);

        group.MapPut("/{id:guid}/recalculate", async (
            Guid id, ICommandHandler<RecalculateBudgetLineCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new RecalculateBudgetLineCommand(id), ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.ManageBudget);
    }
}
