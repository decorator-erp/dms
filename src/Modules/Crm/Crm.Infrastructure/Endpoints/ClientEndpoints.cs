using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Crm.Contracts;
using Crm.Contracts.Clients;
using Crm.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Crm.Infrastructure.Endpoints;

/// <summary>Client CRUD, governed by <see cref="CrmPermissions.Clients"/> — deliberately its own
/// top-level prefix, not under <c>/api/crm</c>, matching the permission strings' own non-uniform
/// naming (ported as found, per <c>CrmPermissions</c>'s own remarks).</summary>
internal sealed class ClientEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder clients = app.MapGroup("/api/clients").WithTags("Clients");

        clients.MapPost("/", async (
            CreateClientCommand command, ICommandHandler<CreateClientCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(command, ct);
            return result.Match(id => Results.Created($"/api/clients/{id}", new { ClientId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Clients.Create);

        clients.MapPut("/{clientId:guid}/profile", async (
            Guid clientId, UpdateProfileRequest request, ICommandHandler<UpdateClientProfileCommand> handler, ICurrentUser currentUser, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result result = await handler.HandleAsync(
                new UpdateClientProfileCommand(clientId, userId.Value!, request.JobTitle, request.MaritalStatus, request.RepresentativeName), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Clients.Update);

        clients.MapPut("/{clientId:guid}/active", async (
            Guid clientId, SetActiveRequest request, ICommandHandler<SetClientActiveCommand> handler, ICurrentUser currentUser, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result result = await handler.HandleAsync(new SetClientActiveCommand(clientId, request.IsActive, userId.Value!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Clients.SetActive);

        clients.MapDelete("/{clientId:guid}", async (
            Guid clientId, ICommandHandler<DeleteClientCommand> handler, ICurrentUser currentUser, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result result = await handler.HandleAsync(new DeleteClientCommand(clientId, userId.Value!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Clients.Delete);

        clients.MapPost("/{clientId:guid}/children", async (
            Guid clientId, ClientChildRequest request, ICommandHandler<AddClientChildCommand, Guid> handler, ICurrentUser currentUser, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result<Guid> result = await handler.HandleAsync(new AddClientChildCommand(clientId, request.Gender, request.Age, userId.Value!), ct);
            return result.Match(id => Results.Created($"/api/clients/{clientId}/children/{id}", new { ChildId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Clients.ManageChildren);

        clients.MapPut("/{clientId:guid}/children/{childId:guid}", async (
            Guid clientId, Guid childId, ClientChildRequest request,
            ICommandHandler<UpdateClientChildCommand> handler, ICurrentUser currentUser, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result result = await handler.HandleAsync(new UpdateClientChildCommand(clientId, childId, request.Gender, request.Age, userId.Value!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Clients.ManageChildren);

        clients.MapDelete("/{clientId:guid}/children/{childId:guid}", async (
            Guid clientId, Guid childId, ICommandHandler<RemoveClientChildCommand> handler, ICurrentUser currentUser, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result result = await handler.HandleAsync(new RemoveClientChildCommand(clientId, childId, userId.Value!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Clients.ManageChildren);

        clients.MapGet("/{clientId:guid}", async (
            Guid clientId, IQueryHandler<GetClientByIdQuery, ClientResponse> handler, ICurrentUser currentUser, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result<ClientResponse> result = await handler.HandleAsync(new GetClientByIdQuery(clientId, userId.Value!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Clients.Read);

        clients.MapGet("/", async (
            int? pageNumber, int? pageSize, string? search, string? isActive, ICurrentUser currentUser,
            IQueryHandler<GetClientsQuery, PagedResult<ClientListItemResponse>> handler, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            // C-3: an omitted isActive lists active clients only; isActive=false lists the deactivated ones.
            // D-16 / C-F5: isActive=all lists both. Any other value is a 400.
            Result<bool?> activeFilter = ParseIsActive(isActive);
            if (activeFilter.IsFailure)
            {
                return activeFilter.ToProblem();
            }

            Result<PagedResult<ClientListItemResponse>> result = await handler.HandleAsync(
                new GetClientsQuery(userId.Value!, pageNumber ?? 1, pageSize ?? 50, search, activeFilter.Value), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Clients.Read);
    }

    private static Result<bool?> ParseIsActive(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        null or "" or "TRUE" => Result.Success<bool?>(true),
        "FALSE" => Result.Success<bool?>(false),
        "ALL" => Result.Success<bool?>(null),
        _ => Result.Failure<bool?>(ClientErrors.InvalidActiveFilter),
    };

    private sealed record UpdateProfileRequest(string? JobTitle, string? MaritalStatus, string? RepresentativeName);
    private sealed record SetActiveRequest(bool IsActive);
    private sealed record ClientChildRequest(char Gender, short Age);
}
