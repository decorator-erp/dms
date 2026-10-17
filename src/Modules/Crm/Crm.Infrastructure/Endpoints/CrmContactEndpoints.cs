using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Crm.Contracts;
using Crm.Contracts.Contacts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Crm.Infrastructure.Endpoints;

/// <summary>Contact CRUD, client-scoped by route, governed by <see cref="CrmPermissions.Contacts"/>.</summary>
internal sealed class CrmContactEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder contacts = app.MapGroup("/api/crm/clients/{clientId:guid}/contacts").WithTags("Crm Contacts");

        contacts.MapPost("/", async (
            Guid clientId, CreateContactRequest request, ICommandHandler<CreateCrmContactCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(new CreateCrmContactCommand(
                clientId, request.FirstName, request.LastName, request.Role, request.IsPrimary,
                request.Email, request.Phone, request.Mobile, request.Notes), ct);
            return result.Match(
                id => Results.Created($"/api/crm/clients/{clientId}/contacts/{id}", new { ContactId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Contacts.Create);

        contacts.MapPut("/{contactId:guid}", async (
            Guid contactId, UpdateContactRequest request, ICommandHandler<UpdateCrmContactCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new UpdateCrmContactCommand(
                contactId, request.FirstName, request.LastName, request.Role,
                request.Email, request.Phone, request.Mobile, request.Notes), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Contacts.Update);

        contacts.MapPut("/{contactId:guid}/primary", async (
            Guid clientId, Guid contactId, ICommandHandler<SetPrimaryCrmContactCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new SetPrimaryCrmContactCommand(clientId, contactId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Contacts.SetPrimary);

        contacts.MapGet("/", async (
            Guid clientId, IQueryHandler<GetCrmContactsByClientQuery, IReadOnlyList<CrmContactResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<CrmContactResponse>> result = await handler.HandleAsync(new GetCrmContactsByClientQuery(clientId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Contacts.Read);
    }

    private sealed record CreateContactRequest(
        string FirstName, string LastName, ContactRole Role, bool IsPrimary,
        string? Email = null, string? Phone = null, string? Mobile = null, string? Notes = null);

    private sealed record UpdateContactRequest(
        string FirstName, string LastName, ContactRole Role, string? Email, string? Phone, string? Mobile, string? Notes);
}
