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
using SharedKernel;

namespace Projects.Infrastructure.Endpoints;

/// <summary>Runtime <c>ProjectStage</c> lifecycle. <c>ProjectId</c>/<c>UserId</c> are populated from the
/// route and <see cref="ICurrentUser"/> respectively, same as every other <see cref="IProjectScoped"/>
/// route in this module.</summary>
internal sealed class StageEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/projects/{projectId:guid}/stages").WithTags("ProjectStages");

        group.MapGet("/", async (
            Guid projectId, ICurrentUser currentUser,
            IQueryHandler<GetProjectStagesByProjectQuery, IReadOnlyList<ProjectStageResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<ProjectStageResponse>> result = await handler.HandleAsync(new GetProjectStagesByProjectQuery(projectId, currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Read);

        group.MapGet("/{stageId:guid}", async (
            Guid projectId, Guid stageId, ICurrentUser currentUser,
            IQueryHandler<GetProjectStageQuery, ProjectStageResponse> handler, CancellationToken ct) =>
        {
            Result<ProjectStageResponse> result = await handler.HandleAsync(new GetProjectStageQuery(projectId, stageId, currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Read);

        // E2 (D2): manual stages. stages.manage, the module's existing code for runtime ProjectStage writes.
        group.MapPost("/", async (
            Guid projectId, StageRequest request, ICurrentUser currentUser,
            ICommandHandler<CreateProjectStageCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(new CreateProjectStageCommand(
                projectId, currentUser.UserId!, request.Name ?? string.Empty, request.Order ?? int.MaxValue,
                request.Description, request.Notes, request.EstimatedStartDate, request.EstimatedEndDate,
                request.TemplateStageId ?? request.StageId), ct);
            return result.Match(stageId => Results.Ok(new { StageId = stageId }), r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Manage);

        group.MapPut("/{stageId:guid}", async (
            Guid projectId, Guid stageId, StageRequest request, ICurrentUser currentUser,
            ICommandHandler<UpdateProjectStageCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new UpdateProjectStageCommand(
                projectId, stageId, currentUser.UserId!, request.Name ?? string.Empty, request.Order,
                request.Description, request.Notes, request.EstimatedStartDate, request.EstimatedEndDate), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Manage);

        group.MapDelete("/{stageId:guid}", async (
            Guid projectId, Guid stageId, ICurrentUser currentUser,
            ICommandHandler<DeleteProjectStageCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new DeleteProjectStageCommand(projectId, stageId, currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Manage);

        group.MapPost("/{stageId:guid}/complete", async (
            Guid projectId, Guid stageId, ICurrentUser currentUser,
            ICommandHandler<CompleteProjectStageCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new CompleteProjectStageCommand(projectId, stageId, currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Complete);

        group.MapPost("/{stageId:guid}/reject", async (
            Guid projectId, Guid stageId, RejectStageRequest request, ICurrentUser currentUser,
            ICommandHandler<RejectProjectStageCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new RejectProjectStageCommand(projectId, stageId, currentUser.UserId!, request.Reason), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Reject);
    }

    private sealed record RejectStageRequest(string Reason);

    /// <summary>The frontend's <c>CreateProjectStageRequest</c>/<c>UpdateProjectStageRequest</c>: its
    /// <c>stageId</c> is the template stage; <c>templateStageId</c> is accepted as the clearer name. A
    /// missing order means "at the end". Update ignores the template stage.</summary>
    private sealed record StageRequest(
        string? Name, int? Order, string? Description, string? Notes,
        DateTime? EstimatedStartDate, DateTime? EstimatedEndDate, Guid? StageId, Guid? TemplateStageId);
}
