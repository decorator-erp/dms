using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Projects.Contracts;
using Projects.Contracts.Projects;

namespace Projects.Infrastructure.Endpoints;

/// <summary><see cref="IProjectScoped.UserId"/> is ALWAYS populated here from <see cref="ICurrentUser"/>,
/// never bound from the request body — the request DTOs below deliberately carry no user field at all,
/// so there is nothing to spoof (G5-07's own explicit test proves this at the Application layer; this is
/// the endpoint half of that guarantee).</summary>
internal sealed class ProjectEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects").WithTags("Projects");

        group.MapGet("/", async (
            int pageNumber, int pageSize, string? nameFilter, ICurrentUser currentUser,
            IQueryHandler<GetProjectsQuery, PagedResult<ProjectResponse>> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetProjectsQuery(pageNumber, pageSize, currentUser.UserId!, nameFilter), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Projects.Read);

        group.MapGet("/{projectId:guid}", async (
            Guid projectId, ICurrentUser currentUser,
            IQueryHandler<GetProjectQuery, ProjectResponse> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetProjectQuery(projectId, currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Projects.Read);

        group.MapPost("/", async (
            CreateProjectCommand command, ICommandHandler<CreateProjectCommand, Guid> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(command, ct);
            return result.Match(projectId => Results.Ok(new { ProjectId = projectId }), r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Projects.Create);

        group.MapPut("/{projectId:guid}", async (
            Guid projectId, UpdateProjectRequest request, ICurrentUser currentUser,
            ICommandHandler<UpdateProjectCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new UpdateProjectCommand(
                projectId, currentUser.UserId!, request.Name, request.Description, request.Address,
                request.Notes, request.EstimatedStartDate, request.EstimatedEndDate), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Projects.Update);

        group.MapPut("/{projectId:guid}/status", async (
            Guid projectId, ProjectStatus status, ICurrentUser currentUser,
            ICommandHandler<ChangeProjectStatusCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new ChangeProjectStatusCommand(projectId, currentUser.UserId!, status), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Projects.ChangeStatus);

        group.MapPut("/{projectId:guid}/active", async (
            Guid projectId, bool isActive, ICurrentUser currentUser,
            ICommandHandler<SetProjectActiveCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new SetProjectActiveCommand(projectId, currentUser.UserId!, isActive), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Projects.SetActive);

        group.MapDelete("/{projectId:guid}", async (
            Guid projectId, ICurrentUser currentUser,
            ICommandHandler<DeleteProjectCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new DeleteProjectCommand(projectId, currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Projects.Delete);
    }

    private sealed record UpdateProjectRequest(
        string? Name, string? Description, string? Address, string? Notes,
        DateTime? EstimatedStartDate, DateTime? EstimatedEndDate);
}
