using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Projects.Contracts;
using Projects.Contracts.Grants;
using SharedKernel;

namespace Projects.Infrastructure.Endpoints;

/// <summary>
/// P1-A: project access grants. The route claim is <c>project.read</c>, which everyone who works on projects holds;
/// who may manage grants (a <c>project.manage_access</c> holder or a Manager by membership) is the handlers'
/// decision, so a project Manager without the code still can: route claim 403 → project 404 → not a grant manager
/// 403 → grant state 409. The caller comes from <see cref="ICurrentUser"/>; the bodies carry no user field.
/// </summary>
internal sealed class ProjectAccessGrantEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/projects/{projectId:guid}/access-grants").WithTags("ProjectAccessGrants");

        group.MapGet("/", async (
            Guid projectId, ICurrentUser currentUser,
            IQueryHandler<GetProjectAccessGrantsQuery, IReadOnlyList<ProjectAccessGrantResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<ProjectAccessGrantResponse>> result =
                await handler.HandleAsync(new GetProjectAccessGrantsQuery(projectId, currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Projects.Read);

        group.MapPost("/", async (
            Guid projectId, CreateGrantRequest request, ICurrentUser currentUser,
            ICommandHandler<CreateProjectAccessGrantCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(new CreateProjectAccessGrantCommand(
                projectId, currentUser.UserId!, request.EmployeeId, request.Level, request.Areas, request.StageIds ?? [],
                request.StartsAtUtc, request.ExpiresAtUtc, request.Reason ?? string.Empty), ct);
            return result.Match(grantId => Results.Ok(new { GrantId = grantId }), r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Projects.Read);

        group.MapPut("/{grantId:guid}", async (
            Guid projectId, Guid grantId, UpdateGrantRequest request, ICurrentUser currentUser,
            ICommandHandler<UpdateProjectAccessGrantCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new UpdateProjectAccessGrantCommand(
                projectId, currentUser.UserId!, grantId, request.Level, request.Areas, request.StageIds ?? [],
                request.ExpiresAtUtc, request.Reason ?? string.Empty), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Projects.Read);

        group.MapPost("/{grantId:guid}/revoke", async (
            Guid projectId, Guid grantId, RevokeGrantRequest? request, ICurrentUser currentUser,
            ICommandHandler<RevokeProjectAccessGrantCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new RevokeProjectAccessGrantCommand(projectId, currentUser.UserId!, grantId, request?.Reason), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Projects.Read);

        // "Shared with me": the caller's own grants, so a signed-in caller and no particular permission (Rule 12's
        // authenticated-only list): a grantee may hold no project code in their token at all.
        app.MapGet("/api/me/access-grants", async (
            ICurrentUser currentUser,
            IQueryHandler<GetMyAccessGrantsQuery, IReadOnlyList<MyAccessGrantResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<MyAccessGrantResponse>> result =
                await handler.HandleAsync(new GetMyAccessGrantsQuery(currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).WithTags("ProjectAccessGrants").RequireAuthorization();
    }

    private sealed record CreateGrantRequest(
        Guid EmployeeId, ProjectMemberAccess Level, ProjectAccessArea Areas, IReadOnlyList<Guid>? StageIds,
        DateTime? StartsAtUtc, DateTime? ExpiresAtUtc, string? Reason);

    private sealed record UpdateGrantRequest(
        ProjectMemberAccess Level, ProjectAccessArea Areas, IReadOnlyList<Guid>? StageIds, DateTime? ExpiresAtUtc,
        string? Reason);

    private sealed record RevokeGrantRequest(string? Reason);
}
