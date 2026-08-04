using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Projects.Contracts;
using Projects.Contracts.Stages;

namespace Projects.Infrastructure.Endpoints;

/// <summary>Runtime <c>ProjectStage</c> lifecycle. <c>ProjectId</c>/<c>UserId</c> are populated from the
/// route and <see cref="ICurrentUser"/> respectively, same as every other <see cref="IProjectScoped"/>
/// route in this module.</summary>
internal sealed class StageEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects/{projectId:guid}/stages").WithTags("ProjectStages");

        group.MapGet("/", async (
            Guid projectId, ICurrentUser currentUser,
            IQueryHandler<GetProjectStagesByProjectQuery, IReadOnlyList<ProjectStageResponse>> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetProjectStagesByProjectQuery(projectId, currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Read);

        group.MapGet("/{stageId:guid}", async (
            Guid projectId, Guid stageId, ICurrentUser currentUser,
            IQueryHandler<GetProjectStageQuery, ProjectStageResponse> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetProjectStageQuery(projectId, stageId, currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Read);

        group.MapPost("/{stageId:guid}/complete", async (
            Guid projectId, Guid stageId, ICurrentUser currentUser,
            ICommandHandler<CompleteProjectStageCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new CompleteProjectStageCommand(projectId, stageId, currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Complete);

        group.MapPost("/{stageId:guid}/reject", async (
            Guid projectId, Guid stageId, RejectStageRequest request, ICurrentUser currentUser,
            ICommandHandler<RejectProjectStageCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new RejectProjectStageCommand(projectId, stageId, currentUser.UserId!, request.Reason), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Reject);
    }

    private sealed record RejectStageRequest(string Reason);
}
