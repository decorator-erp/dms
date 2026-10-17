using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Crm.Contracts;
using Crm.Contracts.Clients;
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
            Guid clientId, UpdateProfileRequest request, ICommandHandler<UpdateClientProfileCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new UpdateClientProfileCommand(clientId, request.JobTitle, request.MaritalStatus, request.RepresentativeName), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Clients.Update);

        clients.MapPut("/{clientId:guid}/active", async (
            Guid clientId, SetActiveRequest request, ICommandHandler<SetClientActiveCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new SetClientActiveCommand(clientId, request.IsActive), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Clients.SetActive);

        clients.MapDelete("/{clientId:guid}", async (
            Guid clientId, ICommandHandler<DeleteClientCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new DeleteClientCommand(clientId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Clients.Delete);

        clients.MapPost("/{clientId:guid}/children", async (
            Guid clientId, ClientChildRequest request, ICommandHandler<AddClientChildCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(new AddClientChildCommand(clientId, request.Gender, request.Age), ct);
            return result.Match(id => Results.Created($"/api/clients/{clientId}/children/{id}", new { ChildId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Clients.ManageChildren);

        clients.MapPut("/{clientId:guid}/children/{childId:guid}", async (
            Guid clientId, Guid childId, ClientChildRequest request,
            ICommandHandler<UpdateClientChildCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new UpdateClientChildCommand(clientId, childId, request.Gender, request.Age), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Clients.ManageChildren);

        clients.MapDelete("/{clientId:guid}/children/{childId:guid}", async (
            Guid clientId, Guid childId, ICommandHandler<RemoveClientChildCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new RemoveClientChildCommand(clientId, childId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Clients.ManageChildren);

        clients.MapGet("/{clientId:guid}", async (
            Guid clientId, IQueryHandler<GetClientByIdQuery, ClientResponse> handler, CancellationToken ct) =>
        {
            Result<ClientResponse> result = await handler.HandleAsync(new GetClientByIdQuery(clientId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Clients.Read);

        clients.MapGet("/", async (
            IQueryHandler<GetClientsQuery, IReadOnlyList<ClientResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<ClientResponse>> result = await handler.HandleAsync(new GetClientsQuery(), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Clients.Read);
    }

    private sealed record UpdateProfileRequest(string? JobTitle, string? MaritalStatus, string? RepresentativeName);
    private sealed record SetActiveRequest(bool IsActive);
    private sealed record ClientChildRequest(char Gender, short Age);
}
