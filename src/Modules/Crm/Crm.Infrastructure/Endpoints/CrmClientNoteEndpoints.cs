using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Crm.Contracts;
using Crm.Contracts.Notes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Crm.Infrastructure.Endpoints;

/// <summary>Client note CRUD, governed by <see cref="CrmPermissions.Notes"/> — internal-only, per
/// <c>CrmClientNoteResponse</c>'s own remarks: never a Portal route for this feature.
/// <see cref="CreateCrmClientNoteCommand.WrittenById"/> is resolved from the caller via
/// <see cref="CrmEndpointActors.ResolveEmployeeIdAsync"/>, never client-supplied — the contract takes a
/// pre-resolved employee id (set at G9-01, before <c>ICrmAccessGuard</c> existed).</summary>
internal sealed class CrmClientNoteEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var byClient = app.MapGroup("/api/crm/clients/{clientId:guid}/notes").WithTags("Crm Client Notes");

        byClient.MapPost("/", async (
            Guid clientId, CreateNoteRequest request, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<CreateCrmClientNoteCommand, Guid> handler, CancellationToken ct) =>
        {
            var writtenById = await CrmEndpointActors.ResolveEmployeeIdAsync(guard, currentUser.UserId, ct);
            if (writtenById.IsFailure)
                return writtenById.ToProblem();

            var result = await handler.HandleAsync(
                new CreateCrmClientNoteCommand(clientId, request.ProjectId, request.Content, writtenById.Value), ct);
            return result.Match(
                id => Results.Created($"/api/crm/notes/{id}", new { NoteId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Notes.Create);

        byClient.MapGet("/", async (
            Guid clientId, IQueryHandler<GetCrmClientNotesByClientQuery, IReadOnlyList<CrmClientNoteResponse>> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetCrmClientNotesByClientQuery(clientId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Notes.Read);

        var byId = app.MapGroup("/api/crm/notes/{noteId:guid}").WithTags("Crm Client Notes");

        byId.MapPut("/", async (
            Guid noteId, UpdateNoteRequest request, ICommandHandler<UpdateCrmClientNoteCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new UpdateCrmClientNoteCommand(noteId, request.Content), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Notes.Update);

        byId.MapDelete("/", async (
            Guid noteId, ICommandHandler<DeleteCrmClientNoteCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new DeleteCrmClientNoteCommand(noteId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Notes.Delete);
    }

    private sealed record CreateNoteRequest(string Content, Guid? ProjectId = null);
    private sealed record UpdateNoteRequest(string Content);
}
