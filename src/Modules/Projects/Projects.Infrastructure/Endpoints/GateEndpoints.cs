using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Projects.Contracts;
using Projects.Contracts.Gates;
using SharedKernel;

namespace Projects.Infrastructure.Endpoints;

/// <summary>Gate CRUD, governed by <c>stages.manage_gates</c> (stage-owned) /
/// <c>stage_tasks.manage_gates</c> (task-owned), per decision #9. Delete and satisfy share one route for both
/// owners, so it admits either claim and the handler checks the one the gate's owner needs (P4).</summary>
internal sealed class GateEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder project = app.MapGroup("/api/projects/{projectId:guid}").WithTags("GateConditions");

        project.MapGet("/stages/{stageId:guid}/gates", async (
            Guid projectId, Guid stageId, ICurrentUser currentUser,
            IQueryHandler<GetStageGateConditionsQuery, IReadOnlyList<GateConditionResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<GateConditionResponse>> result = await handler.HandleAsync(new GetStageGateConditionsQuery(projectId, stageId, currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.ManageGates);

        project.MapPost("/stages/{stageId:guid}/gates", async (
            Guid projectId, Guid stageId, CreateGateRequest request, ICurrentUser currentUser,
            ICommandHandler<CreateStageGateConditionCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(new CreateStageGateConditionCommand(
                projectId, stageId, currentUser.UserId!, request.ConditionType, request.Trigger, request.Label,
                request.ReferencedTaskId, request.ReferencedStageId, request.ApprovalType, request.ReferencedDocumentId), ct);
            return result.Match(id => Results.Ok(new { GateConditionId = id }), r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.ManageGates);

        project.MapGet("/tasks/{taskId:guid}/gates", async (
            Guid projectId, Guid taskId, ICurrentUser currentUser,
            IQueryHandler<GetTaskGateConditionsQuery, IReadOnlyList<GateConditionResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<GateConditionResponse>> result = await handler.HandleAsync(new GetTaskGateConditionsQuery(projectId, taskId, currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.ManageGates);

        project.MapPost("/tasks/{taskId:guid}/gates", async (
            Guid projectId, Guid taskId, CreateGateRequest request, ICurrentUser currentUser,
            ICommandHandler<CreateTaskGateConditionCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(new CreateTaskGateConditionCommand(
                projectId, taskId, currentUser.UserId!, request.ConditionType, request.Trigger, request.Label,
                request.ReferencedTaskId, request.ReferencedStageId, request.ApprovalType, request.ReferencedDocumentId), ct);
            return result.Match(id => Results.Ok(new { GateConditionId = id }), r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.ManageGates);

        project.MapDelete("/gates/{gateConditionId:guid}", async (
            Guid projectId, Guid gateConditionId, ICurrentUser currentUser,
            ICommandHandler<DeleteGateConditionCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new DeleteGateConditionCommand(projectId, gateConditionId, currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequireAnyPermission(ProjectsPermissions.Stages.ManageGates, ProjectsPermissions.StageTasks.ManageGates);

        project.MapPost("/gates/{gateConditionId:guid}/satisfy", async (
            Guid projectId, Guid gateConditionId, SatisfyGateRequest request, ICurrentUser currentUser,
            ICommandHandler<SatisfyGateConditionCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new SatisfyGateConditionCommand(projectId, gateConditionId, currentUser.UserId!, request.Note), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequireAnyPermission(ProjectsPermissions.Stages.ManageGates, ProjectsPermissions.StageTasks.ManageGates);
    }

    private sealed record CreateGateRequest(
        ConditionType ConditionType, GateTrigger Trigger, string Label,
        Guid? ReferencedTaskId, Guid? ReferencedStageId, ApprovalType? ApprovalType,
        Guid? ReferencedDocumentId = null);

    private sealed record SatisfyGateRequest(string? Note);
}
