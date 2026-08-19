using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Finance.Contracts;
using Finance.Contracts.Financial;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Finance.Infrastructure.Endpoints;

/// <summary>
/// The stage drilldowns — four routes under <c>/api/project-stages</c>, served by Finance. Same
/// route-shape-versus-ownership point as <see cref="ProjectFinancialEndpoints"/>, and Projects maps no
/// group on this prefix at all today, so Finance is its only occupant.
///
/// <para>All four are unpaged: a stage's drilldowns are bounded by the stage, which is itself bounded by
/// its project. The project-scope equivalents ARE paged, because a project can accumulate an unbounded
/// number of movements over its life.</para>
/// </summary>
internal sealed class ProjectStageFinancialEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project-stages").WithTags("ProjectStageFinancial");

        group.MapGet("/{id:guid}/financial/movements", async (
            Guid id, DateOnly? fromDate, DateOnly? toDate, LedgerEntryType? type,
            IQueryHandler<GetStageFinancialMovementsQuery, IReadOnlyList<LedgerMovementResponse>> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new GetStageFinancialMovementsQuery(id, fromDate, toDate, type), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        group.MapGet("/{id:guid}/invoices", async (
            Guid id, InvoiceStatus? status, InvoiceType? type,
            IQueryHandler<GetStageInvoicesQuery, IReadOnlyList<InvoiceListItemResponse>> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetStageInvoicesQuery(id, status, type), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        group.MapGet("/{id:guid}/variations", async (
            Guid id, VariationStatus? status,
            IQueryHandler<GetStageVariationsQuery, IReadOnlyList<VariationListItemResponse>> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetStageVariationsQuery(id, status), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        group.MapGet("/{id:guid}/boq-snapshots", async (
            Guid id, BoqSnapshotType? type,
            IQueryHandler<GetStageBoqSnapshotsQuery, IReadOnlyList<BoqSnapshotSummaryResponse>> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetStageBoqSnapshotsQuery(id, type), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);
    }
}
