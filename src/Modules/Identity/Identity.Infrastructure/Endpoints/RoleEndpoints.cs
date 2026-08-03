using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Identity.Contracts;
using Identity.Contracts.Roles;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Identity.Infrastructure.Endpoints;

internal sealed class RoleEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/roles").WithTags("Roles");

        group.MapGet("/", async (IQueryHandler<GetAllRolesQuery, IReadOnlyList<RoleResponse>> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetAllRolesQuery(), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(IdentityPermissions.RolesRead);

        group.MapGet("/{roleId}", async (
            string roleId, IQueryHandler<GetRoleQuery, RoleResponse> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetRoleQuery(roleId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(IdentityPermissions.RolesRead);

        group.MapPost("/", async (
            CreateRoleCommand command, ICommandHandler<CreateRoleCommand, string> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(command, ct);
            return result.Match(roleId => Results.Ok(new { RoleId = roleId }), r => r.ToProblem());
        }).RequirePermission(IdentityPermissions.RolesManage);

        group.MapPut("/{roleId}", async (
            string roleId, UpdateRoleRequest request, ICommandHandler<UpdateRoleCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new UpdateRoleCommand(roleId, request.Name, request.Description), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(IdentityPermissions.RolesManage);

        group.MapDelete("/{roleId}", async (
            string roleId, ICommandHandler<DeleteRoleCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new DeleteRoleCommand(roleId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(IdentityPermissions.RolesManage);
    }

    /// <summary>The route already carries <c>roleId</c> — the body only needs the fields
    /// <see cref="UpdateRoleCommand"/> doesn't get from the URL.</summary>
    private sealed record UpdateRoleRequest(string Name, string? Description);
}
