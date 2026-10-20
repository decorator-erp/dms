using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Identity.Contracts;
using Identity.Contracts.Roles;
using Identity.Contracts.Users;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Identity.Infrastructure.Endpoints;

internal sealed class RoleEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/roles").WithTags("Roles");

        group.MapGet("/", async (IQueryHandler<GetAllRolesQuery, IReadOnlyList<RoleResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<RoleResponse>> result = await handler.HandleAsync(new GetAllRolesQuery(), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(IdentityPermissions.RolesRead);

        group.MapGet("/{roleId}", async (
            string roleId, IQueryHandler<GetRoleQuery, RoleResponse> handler, CancellationToken ct) =>
        {
            Result<RoleResponse> result = await handler.HandleAsync(new GetRoleQuery(roleId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(IdentityPermissions.RolesRead);

        group.MapPost("/", async (
            CreateRoleCommand command, ICommandHandler<CreateRoleCommand, string> handler, CancellationToken ct) =>
        {
            Result<string> result = await handler.HandleAsync(command, ct);
            return result.Match(roleId => Results.Ok(new { RoleId = roleId }), r => r.ToProblem());
        }).RequirePermission(IdentityPermissions.RolesManage);

        group.MapPut("/{roleId}", async (
            string roleId, UpdateRoleRequest request, ICommandHandler<UpdateRoleCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new UpdateRoleCommand(roleId, request.Name, request.Description), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(IdentityPermissions.RolesManage);

        group.MapDelete("/{roleId}", async (
            string roleId, ICommandHandler<DeleteRoleCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new DeleteRoleCommand(roleId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(IdentityPermissions.RolesManage);

        // AssignRoleToUserAsync / RemoveRoleFromUserAsync / GetUserRolesAsync already existed on
        // IRoleService, reachable from no route: a user's roles could never be changed through the API.
        // "/users/{userId}" is a literal segment before its parameter, distinct from the bare
        // "/{roleId}" above, so there is no route-matching ambiguity between the two shapes.
        group.MapGet("/users/{userId}", async (
            string userId, IQueryHandler<GetUserRolesQuery, IReadOnlyList<RoleResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<RoleResponse>> result = await handler.HandleAsync(new GetUserRolesQuery(userId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequireAuthorization(); // N6: self or roles.read, decided in GetUserRolesQueryHandler (404 otherwise).

        // N6: the members of a role, paged.
        group.MapGet("/{roleId}/users", async (
            string roleId, int? page, int? pageSize,
            IQueryHandler<GetRoleUsersQuery, PagedResult<UserListItem>> handler, CancellationToken ct) =>
        {
            Result<PagedResult<UserListItem>> result = await handler.HandleAsync(
                new GetRoleUsersQuery(roleId, page ?? 1, pageSize ?? 50), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(IdentityPermissions.RolesRead);

        group.MapPost("/{roleId}/users/{userId}", async (
            string roleId, string userId, ICommandHandler<AssignRoleToUserCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new AssignRoleToUserCommand(userId, roleId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(IdentityPermissions.RolesManage);

        group.MapDelete("/{roleId}/users/{userId}", async (
            string roleId, string userId, ICommandHandler<RemoveRoleFromUserCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new RemoveRoleFromUserCommand(userId, roleId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(IdentityPermissions.RolesManage);
    }

    /// <summary>The route already carries <c>roleId</c> — the body only needs the fields
    /// <see cref="UpdateRoleCommand"/> doesn't get from the URL.</summary>
    private sealed record UpdateRoleRequest(string Name, string? Description);
}
