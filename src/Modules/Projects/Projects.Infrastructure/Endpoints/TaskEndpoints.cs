using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Projects.Contracts;
using Projects.Contracts.Tasks;

namespace Projects.Infrastructure.Endpoints;

/// <summary><c>ProjectTask</c> lifecycle, sub-tasks, and comments — all governed by the
/// <c>stage_tasks.*</c> permission group (decision #9).</summary>
internal sealed class TaskEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects/{projectId:guid}/tasks").WithTags("ProjectTasks");

        group.MapGet("/", async (
            Guid projectId, ICurrentUser currentUser,
            IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetProjectTasksByProjectQuery(projectId, currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Read);

        group.MapGet("/{taskId:guid}", async (
            Guid projectId, Guid taskId, ICurrentUser currentUser,
            IQueryHandler<GetProjectTaskQuery, ProjectTaskResponse> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetProjectTaskQuery(projectId, taskId, currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Read);

        group.MapPost("/{taskId:guid}/assign", async (
            Guid projectId, Guid taskId, AssignTaskRequest request, ICurrentUser currentUser,
            ICommandHandler<AssignProjectTaskCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new AssignProjectTaskCommand(projectId, taskId, currentUser.UserId!, request.EmployeeId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Assign);

        group.MapPost("/{taskId:guid}/start", async (
            Guid projectId, Guid taskId, ICurrentUser currentUser,
            ICommandHandler<StartProjectTaskCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new StartProjectTaskCommand(projectId, taskId, currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Manage);

        group.MapPost("/{taskId:guid}/complete", async (
            Guid projectId, Guid taskId, ICurrentUser currentUser,
            ICommandHandler<CompleteProjectTaskCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new CompleteProjectTaskCommand(projectId, taskId, currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Complete);

        group.MapPost("/{taskId:guid}/reject", async (
            Guid projectId, Guid taskId, RejectTaskRequest request, ICurrentUser currentUser,
            ICommandHandler<RejectProjectTaskCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new RejectProjectTaskCommand(projectId, taskId, currentUser.UserId!, request.Reason), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Manage);

        group.MapPost("/{taskId:guid}/cancel", async (
            Guid projectId, Guid taskId, ICurrentUser currentUser,
            ICommandHandler<CancelProjectTaskCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new CancelProjectTaskCommand(projectId, taskId, currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Manage);

        group.MapPost("/{taskId:guid}/reopen", async (
            Guid projectId, Guid taskId, ICurrentUser currentUser,
            ICommandHandler<ReopenProjectTaskCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new ReopenProjectTaskCommand(projectId, taskId, currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Manage);

        group.MapGet("/{taskId:guid}/sub-tasks", async (
            Guid projectId, Guid taskId, ICurrentUser currentUser,
            IQueryHandler<GetProjectSubTasksByTaskQuery, IReadOnlyList<ProjectSubTaskResponse>> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetProjectSubTasksByTaskQuery(projectId, taskId, currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Read);

        group.MapPost("/sub-tasks/{subTaskId:guid}/complete", async (
            Guid projectId, Guid subTaskId, ICurrentUser currentUser,
            ICommandHandler<CompleteProjectSubTaskCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new CompleteProjectSubTaskCommand(projectId, subTaskId, currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Complete);

        group.MapGet("/{taskId:guid}/comments", async (
            Guid projectId, Guid taskId, ICurrentUser currentUser,
            IQueryHandler<GetTaskCommentsQuery, IReadOnlyList<TaskCommentResponse>> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetTaskCommentsQuery(projectId, taskId, currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Read);

        group.MapPost("/{taskId:guid}/comments", async (
            Guid projectId, Guid taskId, AddCommentRequest request, ICurrentUser currentUser,
            ICommandHandler<AddTaskCommentCommand, Guid> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new AddTaskCommentCommand(projectId, taskId, currentUser.UserId!, request.Content), ct);
            return result.Match(commentId => Results.Ok(new { CommentId = commentId }), r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Manage);
    }

    private sealed record AssignTaskRequest(Guid EmployeeId);
    private sealed record RejectTaskRequest(string Reason);
    private sealed record AddCommentRequest(string Content);
}
