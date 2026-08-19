using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Finance.Contracts;
using Finance.Contracts.LedgerEntries;
using Hr.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Finance.Infrastructure.Endpoints;

/// <summary>
/// The ledger — six routes, and <b>no update and no delete anywhere</b>. Entries are immutable; a
/// mistake is corrected by posting a reversal, which is what <c>POST /{id}/reverse</c> is.
///
/// <para><b><c>finance.reverse_payment</c> is separate from <c>finance.record_payment</c> on purpose.</b>
/// Reversing undoes money that was already recorded — it is the operation an auditor cares about most,
/// and the one a clerk who can record receipts should not necessarily hold. Allocation is
/// <c>record_payment</c>, because settling a payment against an invoice is part of recording it.</para>
/// </summary>
internal sealed class LedgerEntryEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ledger-entries").WithTags("LedgerEntries");

        group.MapPost("/", async (
            PostLedgerEntryRequest request, ICurrentUser currentUser, IHrDirectory directory,
            ICommandHandler<PostLedgerEntryCommand, Guid> handler, CancellationToken ct) =>
        {
            var recordedById = await FinanceEndpointActors.ResolveEmployeeIdAsync(directory, currentUser, ct);
            if (recordedById.IsFailure) return recordedById.ToProblem();

            var result = await handler.HandleAsync(
                new PostLedgerEntryCommand(request, recordedById.Value), ct);

            return result.Match(
                id => Results.Created($"/api/ledger-entries/{id}", new { LedgerEntryId = id }),
                r => r.ToProblem());
        }).RequirePermission(FinancePermissions.RecordPayment);

        group.MapPost("/{id:guid}/reverse", async (
            Guid id, ReverseLedgerEntryRequest request, ICurrentUser currentUser, IHrDirectory directory,
            ICommandHandler<ReverseLedgerEntryCommand, Guid> handler, CancellationToken ct) =>
        {
            var recordedById = await FinanceEndpointActors.ResolveEmployeeIdAsync(directory, currentUser, ct);
            if (recordedById.IsFailure) return recordedById.ToProblem();

            var result = await handler.HandleAsync(
                new ReverseLedgerEntryCommand(id, recordedById.Value, request.Note), ct);

            return result.Match(
                mirrorId => Results.Created($"/api/ledger-entries/{mirrorId}", new { LedgerEntryId = mirrorId }),
                r => r.ToProblem());
        }).RequirePermission(FinancePermissions.ReversePayment);

        group.MapPost("/{id:guid}/allocate", async (
            Guid id, AllocateLedgerEntryRequest request, ICurrentUser currentUser, IHrDirectory directory,
            ICommandHandler<AllocateLedgerEntryCommand> handler, CancellationToken ct) =>
        {
            var actorId = await FinanceEndpointActors.ResolveEmployeeIdAsync(directory, currentUser, ct);
            if (actorId.IsFailure) return actorId.ToProblem();

            var result = await handler.HandleAsync(
                new AllocateLedgerEntryCommand(id, request.InvoiceId, request.Amount, actorId.Value), ct);

            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.RecordPayment);

        group.MapGet("/{id:guid}", async (
            Guid id, IQueryHandler<GetLedgerEntryByIdQuery, LedgerEntryResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetLedgerEntryByIdQuery(id), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        group.MapGet("/by-project/{projectId:guid}", async (
            Guid projectId, LedgerDirection? direction, LedgerEntryType? type,
            Guid? projectStageId, Guid? projectTaskId, DateOnly? fromDate, DateOnly? toDate,
            int? skip, int? take,
            IQueryHandler<GetLedgerEntriesByProjectQuery, PagedResult<LedgerEntryListItemResponse>> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new GetLedgerEntriesByProjectQuery(
                    projectId, direction, type, projectStageId, projectTaskId,
                    fromDate, toDate, skip ?? 0, take ?? 50),
                ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);

        group.MapGet("/by-project/{projectId:guid}/unallocated", async (
            Guid projectId, int? skip, int? take,
            IQueryHandler<GetUnallocatedLedgerEntriesQuery, PagedResult<UnallocatedLedgerEntryResponse>> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new GetUnallocatedLedgerEntriesQuery(projectId, skip ?? 0, take ?? 50), ct);

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(FinancePermissions.Read);
    }
}
