using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Finance.Contracts;
using Finance.Contracts.Dashboard;
using Finance.Contracts.Financial;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Finance.Infrastructure.Endpoints;

/// <summary>
/// <b>ROUTE SHAPE AND MODULE OWNERSHIP ARE INDEPENDENT, and this is the first place in the system where
/// they visibly diverge.</b>
///
/// <para>These eight routes live under <c>/api/projects/{id}/...</c> because that is where a client
/// looking at a project expects its money to be. They are served by <b>Finance</b>, because Finance owns
/// every table they read. Projects contributes nothing but the id in the URL.</para>
///
/// <para>Nothing in the module system objects: <c>MapModuleEndpoints</c> scans the module's own
/// assembly, and two modules mapping groups under one prefix is ordinary ASP.NET routing — the paths
/// are distinct, so there is no conflict to resolve. What would be wrong is the opposite: putting these
/// handlers in Projects so the folder matched the URL, which would make Projects read
/// <c>finance.*</c> tables and break rule 3. <b>The URL is a client-facing information-architecture
/// decision; the module boundary is a data-ownership one. Let them differ.</b> Written up in
/// <c>docs/adding-a-module.md</c> at F2-01.</para>
///
/// <para>All eight are <c>finance.read</c>: they are views onto financial data, and the permission
/// follows the data rather than the URL — a caller with <c>projects.read</c> and no finance permission
/// gets a 403 here, which is correct.</para>
/// </summary>
internal sealed class ProjectFinancialEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Deliberately its own group object rather than one shared with FinanceEndpoints below. Two
        // MapGroup calls on the same prefix are independent route trees; sharing one would couple this
        // file's lifetime to that one's for no benefit.
        RouteGroupBuilder group = app.MapGroup("/api/projects").WithTags("ProjectFinancial");

        group.MapGet("/{id:guid}/financial/summary", async (
            Guid id,
            IQueryHandler<GetProjectFinancialSummaryQuery, ProjectFinancialSummaryResponse> handler,
            CancellationToken ct) =>
        {
            Result<ProjectFinancialSummaryResponse> result = await handler.HandleAsync(new GetProjectFinancialSummaryQuery(id), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        group.MapGet("/{id:guid}/financial/movements", async (
            Guid id, DateOnly? fromDate, DateOnly? toDate, LedgerEntryType? type,
            LedgerDirection? direction, Guid? projectStageId, Guid? projectTaskId,
            Guid? budgetLineId, Guid? variationId, int? skip, int? take,
            IQueryHandler<GetProjectFinancialMovementsQuery, PagedResult<LedgerMovementResponse>> handler,
            CancellationToken ct) =>
        {
            Result<PagedResult<LedgerMovementResponse>> result = await handler.HandleAsync(
                new GetProjectFinancialMovementsQuery(
                    id, fromDate, toDate, type, direction, projectStageId, projectTaskId,
                    budgetLineId, variationId, skip ?? 0, take ?? 50),
                ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        group.MapGet("/{id:guid}/financial/by-category", async (
            Guid id,
            IQueryHandler<GetProjectCostCategoryBreakdownQuery, IReadOnlyList<CostCategoryRow>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<CostCategoryRow>> result = await handler.HandleAsync(new GetProjectCostCategoryBreakdownQuery(id), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        group.MapGet("/{id:guid}/financial/cashflow", async (
            Guid id, DateOnly? fromDate, DateOnly? toDate,
            IQueryHandler<GetProjectCashflowQuery, IReadOnlyList<MonthlyCashRow>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<MonthlyCashRow>> result = await handler.HandleAsync(
                new GetProjectCashflowQuery(id, fromDate, toDate), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        // The dashboard. In the backend this is its own endpoint class (FinanceEndpoints) mapping one
        // route onto the same /api/projects prefix; folded in here because a second class existing only
        // to map one sibling route is a file, not a boundary.
        group.MapGet("/{projectId:guid}/financial-dashboard", async (
            Guid projectId, int? recentActivityCount,
            IQueryHandler<GetProjectFinancialDashboardQuery, ProjectFinancialDashboardResponse> handler,
            CancellationToken ct) =>
        {
            Result<ProjectFinancialDashboardResponse> result = await handler.HandleAsync(
                new GetProjectFinancialDashboardQuery(projectId, recentActivityCount ?? 20), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        group.MapGet("/{id:guid}/invoices", async (
            Guid id, InvoiceStatus? status, InvoiceType? type, Guid? projectStageId,
            int? skip, int? take,
            IQueryHandler<GetProjectInvoicesQuery, PagedResult<InvoiceListItemResponse>> handler,
            CancellationToken ct) =>
        {
            Result<PagedResult<InvoiceListItemResponse>> result = await handler.HandleAsync(
                new GetProjectInvoicesQuery(id, status, type, projectStageId, skip ?? 0, take ?? 50), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        group.MapGet("/{id:guid}/variations", async (
            Guid id, VariationStatus? status, VariationKind? kind,
            Guid? projectStageId, Guid? projectTaskId, int? skip, int? take,
            IQueryHandler<GetProjectVariationsQuery, PagedResult<VariationListItemResponse>> handler,
            CancellationToken ct) =>
        {
            Result<PagedResult<VariationListItemResponse>> result = await handler.HandleAsync(
                new GetProjectVariationsQuery(
                    id, status, kind, projectStageId, projectTaskId, skip ?? 0, take ?? 50),
                ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        group.MapGet("/{id:guid}/boq-snapshots", async (
            Guid id, BoqSnapshotType? type, int? version,
            IQueryHandler<GetProjectBoqSnapshotsQuery, IReadOnlyList<BoqSnapshotSummaryResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<BoqSnapshotSummaryResponse>> result = await handler.HandleAsync(new GetProjectBoqSnapshotsQuery(id, type, version), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);
    }
}
