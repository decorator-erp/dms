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

/// <summary>Space type CRUD (the catalog-facing taxonomy a <c>CrmSpace</c> is typed by), governed by
/// <see cref="CrmPermissions.SpaceTypes"/>.</summary>
internal sealed class CrmSpaceTypeEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder spaceTypes = app.MapGroup("/api/crm/space-types").WithTags("Crm Space Types");

        spaceTypes.MapPost("/", async (
            CreateCrmSpaceTypeCommand command, ICommandHandler<CreateCrmSpaceTypeCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(command, ct);
            return result.Match(id => Results.Created($"/api/crm/space-types/{id}", new { SpaceTypeId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.SpaceTypes.Manage);

        spaceTypes.MapPut("/{spaceTypeId:guid}", async (
            Guid spaceTypeId, UpdateSpaceTypeRequest request, ICommandHandler<UpdateCrmSpaceTypeCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new UpdateCrmSpaceTypeCommand(spaceTypeId, request.NameEn, request.NameAr, request.SortOrder), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.SpaceTypes.Manage);

        spaceTypes.MapPut("/{spaceTypeId:guid}/active", async (
            Guid spaceTypeId, SetActiveRequest request, ICommandHandler<SetCrmSpaceTypeActiveCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new SetCrmSpaceTypeActiveCommand(spaceTypeId, request.IsActive), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.SpaceTypes.Manage);

        spaceTypes.MapGet("/", async (
            IQueryHandler<GetCrmSpaceTypesQuery, IReadOnlyList<CrmSpaceTypeResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<CrmSpaceTypeResponse>> result = await handler.HandleAsync(new GetCrmSpaceTypesQuery(), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.SpaceTypes.Read);
    }

    private sealed record UpdateSpaceTypeRequest(string NameEn, string NameAr, int SortOrder);
    private sealed record SetActiveRequest(bool IsActive);
}
