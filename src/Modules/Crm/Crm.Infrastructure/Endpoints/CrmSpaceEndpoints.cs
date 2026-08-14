using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Crm.Contracts;
using Crm.Contracts.Spaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Crm.Infrastructure.Endpoints;

/// <summary>Project-scoped Space CRUD (a room/area on a project, e.g. "Master Bedroom"), governed by
/// <see cref="CrmPermissions.Spaces"/>. Distinct from a brief-space entry (<c>CrmBriefSpace</c>, see
/// <see cref="CrmBriefEndpoints"/>) — a <c>Space</c> is the durable project fixture; a brief-space entry
/// references one per requirement round.</summary>
internal sealed class CrmSpaceEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var byProject = app.MapGroup("/api/crm/projects/{projectId:guid}/spaces").WithTags("Crm Spaces");

        byProject.MapPost("/", async (
            Guid projectId, CreateSpaceRequest request, ICommandHandler<CreateCrmSpaceCommand, Guid> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new CreateCrmSpaceCommand(
                projectId, request.SpaceTypeId, request.Name, request.SortOrder, request.Description), ct);
            return result.Match(id => Results.Created($"/api/crm/spaces/{id}", new { SpaceId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Spaces.Manage);

        byProject.MapGet("/", async (
            Guid projectId, IQueryHandler<GetCrmSpacesByProjectQuery, IReadOnlyList<CrmSpaceResponse>> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetCrmSpacesByProjectQuery(projectId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Spaces.Read);

        var byId = app.MapGroup("/api/crm/spaces/{spaceId:guid}").WithTags("Crm Spaces");

        byId.MapPut("/", async (
            Guid spaceId, UpdateSpaceRequest request, ICommandHandler<UpdateCrmSpaceCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new UpdateCrmSpaceCommand(spaceId, request.SpaceTypeId, request.Name, request.Description), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Spaces.Manage);

        byId.MapPut("/active", async (
            Guid spaceId, SetActiveRequest request, ICommandHandler<SetCrmSpaceActiveCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new SetCrmSpaceActiveCommand(spaceId, request.IsActive), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Spaces.Manage);
    }

    private sealed record CreateSpaceRequest(Guid SpaceTypeId, string Name, int SortOrder = 0, string? Description = null);
    private sealed record UpdateSpaceRequest(Guid SpaceTypeId, string Name, string? Description);
    private sealed record SetActiveRequest(bool IsActive);
}
