using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Identity.Contracts;
using Identity.Contracts.Permissions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Identity.Infrastructure.Endpoints;

internal sealed class PermissionEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/permissions").WithTags("Permissions");

        // The literal route this task's own verify block names: "assert its strings appear in
        // GET /api/permissions" — the union of every module's registered IModulePermissions.
        group.MapGet("/", async (
            IQueryHandler<GetAllPermissionsQuery, IReadOnlyList<ModulePermissionSet>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<ModulePermissionSet>> result = await handler.HandleAsync(new GetAllPermissionsQuery(), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(IdentityPermissions.PermissionsManage);

        group.MapGet("/roles/{roleId}", async (
            string roleId, IQueryHandler<GetRolePermissionsQuery, IReadOnlyList<PermissionResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<PermissionResponse>> result = await handler.HandleAsync(new GetRolePermissionsQuery(roleId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(IdentityPermissions.RolesRead);

        group.MapPut("/roles/{roleId}/{permission}", async (
            string roleId, string permission, ICommandHandler<AssignPermissionToRoleCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new AssignPermissionToRoleCommand(roleId, permission), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(IdentityPermissions.RolesManage);

        group.MapDelete("/roles/{roleId}/{permission}", async (
            string roleId, string permission, ICommandHandler<RemovePermissionFromRoleCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new RemovePermissionFromRoleCommand(roleId, permission), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(IdentityPermissions.RolesManage);

        group.MapGet("/users/{userId}", async (
            string userId, IQueryHandler<GetUserPermissionsQuery, IReadOnlyList<PermissionResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<PermissionResponse>> result = await handler.HandleAsync(new GetUserPermissionsQuery(userId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(IdentityPermissions.PermissionsManage);

        group.MapGet("/users/{userId}/effective", async (
            string userId, IQueryHandler<GetUserEffectivePermissionsQuery, EffectivePermissionsResponse> handler, CancellationToken ct) =>
        {
            Result<EffectivePermissionsResponse> result = await handler.HandleAsync(new GetUserEffectivePermissionsQuery(userId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequireAuthorization();

        group.MapPost("/users/{userId}/overrides/grant", async (
            string userId, OverrideRequest request, ICommandHandler<GrantPermissionOverrideCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new GrantPermissionOverrideCommand(userId, request.Permission, request.Reason, request.ExpiresAtUtc), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(IdentityPermissions.PermissionsManage);

        group.MapPost("/users/{userId}/overrides/deny", async (
            string userId, OverrideRequest request, ICommandHandler<DenyPermissionOverrideCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new DenyPermissionOverrideCommand(userId, request.Permission, request.Reason, request.ExpiresAtUtc), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(IdentityPermissions.PermissionsManage);

        group.MapDelete("/users/{userId}/overrides/{permission}", async (
            string userId, string permission, ICommandHandler<RevokePermissionOverrideCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new RevokePermissionOverrideCommand(userId, permission), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(IdentityPermissions.PermissionsManage);
    }

    /// <summary>Shared body shape for grant/deny — the route already carries <c>userId</c>.</summary>
    private sealed record OverrideRequest(string Permission, string? Reason = null, DateTime? ExpiresAtUtc = null);
}
