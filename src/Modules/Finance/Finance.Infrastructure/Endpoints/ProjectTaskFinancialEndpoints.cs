using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Finance.Contracts;
using Finance.Contracts.Financial;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Finance.Infrastructure.Endpoints;

/// <summary>
/// The task drilldowns — four routes under <c>/api/project-tasks</c>, served by Finance. Third and last
/// instance of the route-shape-versus-ownership divergence.
///
/// <para><b>Every one of these resolves the task through BOTH routes</b> — named directly, or reached
/// through a budget line the task owns. A posting made against a task's budget line is the more common
/// shape, so a drilldown filtering on the direct column alone would show a task most of its own
/// spending is missing from.</para>
/// </summary>
internal sealed class ProjectTaskFinancialEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/project-tasks").WithTags("ProjectTaskFinancial");

        group.MapGet("/{id:guid}/financial/movements", async (
            Guid id, DateOnly? fromDate, DateOnly? toDate, LedgerEntryType? type,
            IQueryHandler<GetTaskFinancialMovementsQuery, IReadOnlyList<LedgerMovementResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<LedgerMovementResponse>> result = await handler.HandleAsync(
                new GetTaskFinancialMovementsQuery(id, fromDate, toDate, type), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        group.MapGet("/{id:guid}/invoice-lines", async (
            Guid id,
            IQueryHandler<GetTaskInvoiceLinesQuery, IReadOnlyList<TaskInvoiceLineResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<TaskInvoiceLineResponse>> result = await handler.HandleAsync(new GetTaskInvoiceLinesQuery(id), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        group.MapGet("/{id:guid}/boq-lines", async (
            Guid id, int? snapshotVersion,
            IQueryHandler<GetTaskBoqLinesQuery, IReadOnlyList<TaskBoqLineResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<TaskBoqLineResponse>> result = await handler.HandleAsync(new GetTaskBoqLinesQuery(id, snapshotVersion), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        group.MapGet("/{id:guid}/variations", async (
            Guid id, VariationStatus? status,
            IQueryHandler<GetTaskVariationsQuery, IReadOnlyList<VariationListItemResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<VariationListItemResponse>> result = await handler.HandleAsync(new GetTaskVariationsQuery(id, status), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);
    }
}
