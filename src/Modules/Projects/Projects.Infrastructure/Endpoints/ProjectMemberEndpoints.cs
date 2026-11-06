using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Projects.Contracts;
using Projects.Contracts.Members;
using SharedKernel;

namespace Projects.Infrastructure.Endpoints;

/// <summary>E1 (D1): project members. Route claim (403) → project scope decorator (404) → state (409).
/// The caller comes from <see cref="ICurrentUser"/>; the bodies carry no user field.</summary>
internal sealed class ProjectMemberEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/projects/{projectId:guid}/members").WithTags("ProjectMembers");

        group.MapGet("/", async (
            Guid projectId, ICurrentUser currentUser,
            IQueryHandler<GetProjectMembersQuery, IReadOnlyList<ProjectMemberResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<ProjectMemberResponse>> result =
                await handler.HandleAsync(new GetProjectMembersQuery(projectId, currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Projects.Read);

        group.MapPost("/", async (
            Guid projectId, AddMemberRequest request, ICurrentUser currentUser,
            ICommandHandler<AddProjectMemberCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(
                new AddProjectMemberCommand(projectId, currentUser.UserId!, request.EmployeeId, request.Access), ct);
            return result.Match(memberId => Results.Ok(new { MemberId = memberId }), r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Projects.ManageMembers);

        group.MapPut("/{memberId:guid}/access", async (
            Guid projectId, Guid memberId, ChangeAccessRequest request, ICurrentUser currentUser,
            ICommandHandler<ChangeProjectMemberAccessCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new ChangeProjectMemberAccessCommand(projectId, currentUser.UserId!, memberId, request.Access), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Projects.ManageMembers);

        group.MapDelete("/{memberId:guid}", async (
            Guid projectId, Guid memberId, ICurrentUser currentUser,
            ICommandHandler<RemoveProjectMemberCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new RemoveProjectMemberCommand(projectId, currentUser.UserId!, memberId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Projects.ManageMembers);
    }

    private sealed record AddMemberRequest(Guid EmployeeId, ProjectMemberAccess Access);

    private sealed record ChangeAccessRequest(ProjectMemberAccess Access);
}
