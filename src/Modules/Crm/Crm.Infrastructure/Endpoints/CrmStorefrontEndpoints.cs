using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Crm.Contracts;
using Crm.Contracts.Spaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Crm.Infrastructure.Endpoints;

/// <summary>crm-complete F6 (D3): the office storefront for a space. Reads: crm.spaces.read; writes:
/// crm.spaces.manage. No portal write route - the client sees the picks in the brief-space snapshot.</summary>
internal sealed class CrmStorefrontEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/crm/spaces/{spaceId:guid}/selections").WithTags("Crm Storefront");

        group.MapGet("/", async (
            Guid spaceId, ICurrentUser currentUser, IQueryHandler<GetCrmSpaceSelectionsQuery, IReadOnlyList<CrmSpaceSelectionResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<CrmSpaceSelectionResponse>> result = await handler.HandleAsync(new GetCrmSpaceSelectionsQuery(spaceId, currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Spaces.Read);

        group.MapPost("/", async (
            Guid spaceId, SelectionRequest request, ICurrentUser currentUser, ICommandHandler<AddCrmSpaceSelectionCommand, Guid> handler,
            CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(new AddCrmSpaceSelectionCommand(
                spaceId, currentUser.UserId!, request.CatalogItemId ?? Guid.Empty, request.Quantity ?? 0, request.Notes, request.Options ?? []), ct);
            return result.Match(id => Results.Ok(new { SelectionId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Spaces.Manage);

        group.MapPut("/order", async (
            Guid spaceId, SelectionOrderRequest request, ICurrentUser currentUser, ICommandHandler<ReorderCrmSpaceSelectionsCommand> handler,
            CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new ReorderCrmSpaceSelectionsCommand(spaceId, request.SelectionIds ?? [], currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Spaces.Manage);

        group.MapPut("/{selectionId:guid}", async (
            Guid spaceId, Guid selectionId, SelectionRequest request, ICurrentUser currentUser,
            ICommandHandler<UpdateCrmSpaceSelectionCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new UpdateCrmSpaceSelectionCommand(
                spaceId, selectionId, currentUser.UserId!, request.Quantity ?? 0, request.Notes, request.Options), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Spaces.Manage);

        group.MapDelete("/{selectionId:guid}", async (
            Guid spaceId, Guid selectionId, ICurrentUser currentUser, ICommandHandler<RemoveCrmSpaceSelectionCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new RemoveCrmSpaceSelectionCommand(spaceId, selectionId, currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Spaces.Manage);
    }

    private sealed record SelectionRequest(
        Guid? CatalogItemId, decimal? Quantity, string? Notes, IReadOnlyList<SpaceSelectionOptionInput>? Options);

    private sealed record SelectionOrderRequest(IReadOnlyList<Guid>? SelectionIds);
}
