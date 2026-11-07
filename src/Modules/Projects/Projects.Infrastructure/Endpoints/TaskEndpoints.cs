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
using SharedKernel;

namespace Projects.Infrastructure.Endpoints;

/// <summary><c>ProjectTask</c> lifecycle, sub-tasks, and comments — all governed by the
/// <c>stage_tasks.*</c> permission group (decision #9).</summary>
internal sealed class TaskEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/projects/{projectId:guid}/tasks").WithTags("ProjectTasks");

        group.MapGet("/", async (
            Guid projectId, Guid? stageId, Guid? assigneeId, ProjectTaskStatus? status, int? pageNumber, int? pageSize,
            ICurrentUser currentUser,
            IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>> handler, CancellationToken ct) =>
        {
            // P13/R1-5: opt-in paging; the body stays a plain array (every task when neither parameter is sent).
            Result<IReadOnlyList<ProjectTaskResponse>> result = await handler.HandleAsync(
                new GetProjectTasksByProjectQuery(projectId, currentUser.UserId!, stageId, assigneeId, status, pageNumber, pageSize), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Read);

        // E3 (D2): manual tasks. stage_tasks.manage, the module's existing code for ProjectTask writes.
        group.MapPost("/", async (
            Guid projectId, TaskRequest request, ICurrentUser currentUser,
            ICommandHandler<CreateProjectTaskCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(new CreateProjectTaskCommand(
                projectId, currentUser.UserId!, request.ProjectStageId, request.Name ?? string.Empty, request.Description,
                request.Notes, request.AssignedToId, request.Priority ?? TaskPriority.Medium, request.Weight ?? 1.00m,
                request.Order, request.DueDate), ct);
            return result.Match(taskId => Results.Ok(new { TaskId = taskId }), r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Manage);

        group.MapPut("/{taskId:guid}", async (
            Guid projectId, Guid taskId, TaskRequest request, ICurrentUser currentUser,
            ICommandHandler<UpdateProjectTaskCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new UpdateProjectTaskCommand(
                projectId, taskId, currentUser.UserId!, request.Name ?? string.Empty, request.Description, request.Notes,
                request.Priority, request.Weight, request.Order, request.DueDate), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Manage);

        group.MapDelete("/{taskId:guid}", async (
            Guid projectId, Guid taskId, ICurrentUser currentUser,
            ICommandHandler<DeleteProjectTaskCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new DeleteProjectTaskCommand(projectId, taskId, currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Manage);

        group.MapGet("/{taskId:guid}/details", async (
            Guid projectId, Guid taskId, ICurrentUser currentUser,
            IQueryHandler<GetProjectTaskDetailsQuery, ProjectTaskDetailsResponse> handler, CancellationToken ct) =>
        {
            Result<ProjectTaskDetailsResponse> result = await handler.HandleAsync(
                new GetProjectTaskDetailsQuery(projectId, taskId, currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Read);

        group.MapGet("/{taskId:guid}", async (
            Guid projectId, Guid taskId, ICurrentUser currentUser,
            IQueryHandler<GetProjectTaskQuery, ProjectTaskResponse> handler, CancellationToken ct) =>
        {
            Result<ProjectTaskResponse> result = await handler.HandleAsync(new GetProjectTaskQuery(projectId, taskId, currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Read);

        group.MapPost("/{taskId:guid}/assign", async (
            Guid projectId, Guid taskId, AssignTaskRequest request, ICurrentUser currentUser,
            ICommandHandler<AssignProjectTaskCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new AssignProjectTaskCommand(projectId, taskId, currentUser.UserId!, request.EmployeeId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Assign);

        group.MapPost("/{taskId:guid}/start", async (
            Guid projectId, Guid taskId, ICurrentUser currentUser,
            ICommandHandler<StartProjectTaskCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new StartProjectTaskCommand(projectId, taskId, currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequireAnyPermission(ProjectsPermissions.StageTasks.Manage, ProjectsPermissions.StageTasks.Complete); // baseline v5: execution

        group.MapPost("/{taskId:guid}/complete", async (
            Guid projectId, Guid taskId, ICurrentUser currentUser,
            ICommandHandler<CompleteProjectTaskCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new CompleteProjectTaskCommand(projectId, taskId, currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Complete);

        group.MapPost("/{taskId:guid}/reject", async (
            Guid projectId, Guid taskId, RejectTaskRequest request, ICurrentUser currentUser,
            ICommandHandler<RejectProjectTaskCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new RejectProjectTaskCommand(projectId, taskId, currentUser.UserId!, request.Reason), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Manage);

        group.MapPost("/{taskId:guid}/cancel", async (
            Guid projectId, Guid taskId, ICurrentUser currentUser,
            ICommandHandler<CancelProjectTaskCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new CancelProjectTaskCommand(projectId, taskId, currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Manage);

        group.MapPost("/{taskId:guid}/reopen", async (
            Guid projectId, Guid taskId, ICurrentUser currentUser,
            ICommandHandler<ReopenProjectTaskCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new ReopenProjectTaskCommand(projectId, taskId, currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Manage);

        group.MapGet("/{taskId:guid}/sub-tasks", async (
            Guid projectId, Guid taskId, ICurrentUser currentUser,
            IQueryHandler<GetProjectSubTasksByTaskQuery, IReadOnlyList<ProjectSubTaskResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<ProjectSubTaskResponse>> result = await handler.HandleAsync(new GetProjectSubTasksByTaskQuery(projectId, taskId, currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Read);

        // E5: sub-task writes. Create/update/delete: stage_tasks.manage. Reopen: the task's assignee (with
        // stage_tasks.complete) or a manager - checked in the handler after the scope decorator.
        group.MapPost("/{taskId:guid}/sub-tasks", async (
            Guid projectId, Guid taskId, SubTaskRequest request, ICurrentUser currentUser,
            ICommandHandler<CreateProjectSubTaskCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(new CreateProjectSubTaskCommand(
                projectId, taskId, currentUser.UserId!, request.Title ?? string.Empty, request.Description, request.Order,
                request.IsRequired ?? true), ct);
            return result.Match(subTaskId => Results.Ok(new { SubTaskId = subTaskId }), r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Manage);

        group.MapPut("/{taskId:guid}/sub-tasks/{subTaskId:guid}", async (
            Guid projectId, Guid taskId, Guid subTaskId, SubTaskRequest request, ICurrentUser currentUser,
            ICommandHandler<UpdateProjectSubTaskCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new UpdateProjectSubTaskCommand(
                projectId, taskId, subTaskId, currentUser.UserId!, request.Title ?? string.Empty, request.Description,
                request.Order, request.IsRequired), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Manage);

        group.MapDelete("/{taskId:guid}/sub-tasks/{subTaskId:guid}", async (
            Guid projectId, Guid taskId, Guid subTaskId, ICurrentUser currentUser,
            ICommandHandler<DeleteProjectSubTaskCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new DeleteProjectSubTaskCommand(projectId, taskId, subTaskId, currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Manage);

        group.MapPost("/{taskId:guid}/sub-tasks/{subTaskId:guid}/reopen", async (
            Guid projectId, Guid taskId, Guid subTaskId, ICurrentUser currentUser,
            ICommandHandler<ReopenProjectSubTaskCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new ReopenProjectSubTaskCommand(projectId, taskId, subTaskId, currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequireAnyPermission(ProjectsPermissions.StageTasks.Manage, ProjectsPermissions.StageTasks.Complete);

        group.MapPost("/sub-tasks/{subTaskId:guid}/complete", async (
            Guid projectId, Guid subTaskId, ICurrentUser currentUser,
            ICommandHandler<CompleteProjectSubTaskCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new CompleteProjectSubTaskCommand(projectId, subTaskId, currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequireAnyPermission(ProjectsPermissions.StageTasks.Manage, ProjectsPermissions.StageTasks.Complete);

        group.MapGet("/{taskId:guid}/comments", async (
            Guid projectId, Guid taskId, int? pageNumber, int? pageSize, ICurrentUser currentUser,
            IQueryHandler<GetTaskCommentsQuery, IReadOnlyList<TaskCommentResponse>> handler, CancellationToken ct) =>
        {
            // P13/R1-5: opt-in paging; the body stays a plain array (every comment when neither parameter is sent).
            Result<IReadOnlyList<TaskCommentResponse>> result = await handler.HandleAsync(
                new GetTaskCommentsQuery(projectId, taskId, currentUser.UserId!, pageNumber, pageSize), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Read);

        group.MapPost("/{taskId:guid}/comments", async (
            Guid projectId, Guid taskId, AddCommentRequest request, ICurrentUser currentUser,
            ICommandHandler<AddTaskCommentCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(
                new AddTaskCommentCommand(projectId, taskId, currentUser.UserId!, request.Content ?? string.Empty), ct);
            return result.Match(commentId => Results.Ok(new { CommentId = commentId }), r => r.ToProblem());
        }).RequireAnyPermission(ProjectsPermissions.StageTasks.Manage, ProjectsPermissions.StageTasks.Complete); // baseline v5: execution

        // E6: edit and delete a comment. The route needs only stage_tasks.read; the handler allows the author or
        // a stage_tasks.manage holder (403), after the scope decorator (404).
        group.MapPut("/{taskId:guid}/comments/{commentId:guid}", async (
            Guid projectId, Guid taskId, Guid commentId, AddCommentRequest request, ICurrentUser currentUser,
            ICommandHandler<UpdateTaskCommentCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new UpdateTaskCommentCommand(projectId, taskId, commentId, currentUser.UserId!, request.Content ?? string.Empty), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Read);

        group.MapDelete("/{taskId:guid}/comments/{commentId:guid}", async (
            Guid projectId, Guid taskId, Guid commentId, ICurrentUser currentUser,
            ICommandHandler<DeleteTaskCommentCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new DeleteTaskCommentCommand(projectId, taskId, commentId, currentUser.UserId!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.StageTasks.Read);
    }

    private sealed record AssignTaskRequest(Guid EmployeeId);
    private sealed record RejectTaskRequest(string Reason);
    /// <summary>The frontend's <c>CreateProjectTaskRequest</c>/<c>UpdateProjectTaskRequest</c> fields that
    /// the domain has. Update ignores the stage and the assignee (use <c>/assign</c>).</summary>
    private sealed record TaskRequest(
        Guid? ProjectStageId, string? Name, string? Description, string? Notes, Guid? AssignedToId,
        TaskPriority? Priority, decimal? Weight, int? Order, DateTime? DueDate);

    private sealed record SubTaskRequest(string? Title, string? Description, int? Order, bool? IsRequired);

    private sealed record AddCommentRequest(string? Content);
}
