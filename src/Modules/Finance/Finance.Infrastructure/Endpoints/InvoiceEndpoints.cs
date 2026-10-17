using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Finance.Contracts;
using Finance.Contracts.Invoices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Finance.Infrastructure.Endpoints;

/// <summary>
/// Invoices — six routes. <b>Creating is issuing</b>; there is no separate issue step, and none is
/// added here.
///
/// <para><b><c>finance.cancel_invoice</c> is separate from <c>finance.manage_invoice</c>.</b> Cancelling
/// voids a document a client may already hold, so it is the one invoice operation held apart from
/// authoring — the same reasoning that separates reversing a payment from recording one.</para>
/// </summary>
internal sealed class InvoiceEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/invoices").WithTags("Invoices");

        group.MapPost("/", async (
            CreateInvoiceRequest request,
            ICommandHandler<CreateInvoiceCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(new CreateInvoiceCommand(request), ct);

            return result.Match(
                id => Results.Created($"/api/invoices/{id}", new { InvoiceId = id }),
                r => r.ToProblem());
        }).RequirePermission(FinancePermissions.ManageInvoice);

        group.MapPost("/{id:guid}/lines", async (
            Guid id, AddInvoiceLineRequest request,
            ICommandHandler<AddInvoiceLineCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new AddInvoiceLineCommand(id, request.Line), ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.ManageInvoice);

        group.MapPut("/{id:guid}/cancel", async (
            Guid id, ICommandHandler<CancelInvoiceCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new CancelInvoiceCommand(id), ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.CancelInvoice);

        group.MapGet("/{id:guid}", async (
            Guid id, IQueryHandler<GetInvoiceByIdQuery, InvoiceResponse> handler, CancellationToken ct) =>
        {
            Result<InvoiceResponse> result = await handler.HandleAsync(new GetInvoiceByIdQuery(id), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        group.MapGet("/by-project/{projectId:guid}", async (
            Guid projectId, InvoiceStatus? status, int? skip, int? take,
            IQueryHandler<GetInvoicesByProjectQuery, PagedResult<InvoiceListItemResponse>> handler,
            CancellationToken ct) =>
        {
            Result<PagedResult<InvoiceListItemResponse>> result = await handler.HandleAsync(
                new GetInvoicesByProjectQuery(projectId, status, skip ?? 0, take ?? 50), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        group.MapGet("/by-project/{projectId:guid}/outstanding", async (
            Guid projectId,
            IQueryHandler<GetOutstandingInvoicesQuery, IReadOnlyList<InvoiceListItemResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<InvoiceListItemResponse>> result = await handler.HandleAsync(new GetOutstandingInvoicesQuery(projectId), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);
    }
}
