using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Projects.Contracts;
using Projects.Contracts.Templates;
using SharedKernel;

namespace Projects.Infrastructure.Endpoints;

/// <summary>Template-side CRUD — governed by <c>stages.manage</c>/<c>stages.read</c> throughout, per
/// decision #9 (there is no separate template permission group).</summary>
internal sealed class TemplateEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder templates = app.MapGroup("/api/stage-templates").WithTags("StageTemplates");

        templates.MapGet("/", async (
            IQueryHandler<GetStageTemplatesQuery, IReadOnlyList<StageTemplateResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<StageTemplateResponse>> result = await handler.HandleAsync(new GetStageTemplatesQuery(), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Read);

        templates.MapGet("/{stageTemplateId:guid}", async (
            Guid stageTemplateId, IQueryHandler<GetStageTemplateQuery, StageTemplateResponse> handler, CancellationToken ct) =>
        {
            Result<StageTemplateResponse> result = await handler.HandleAsync(new GetStageTemplateQuery(stageTemplateId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Read);

        templates.MapPost("/", async (
            CreateStageTemplateCommand command, ICommandHandler<CreateStageTemplateCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(command, ct);
            return result.Match(id => Results.Ok(new { StageTemplateId = id }), r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Manage);

        templates.MapPut("/{stageTemplateId:guid}", async (
            Guid stageTemplateId, UpdateStageTemplateRequest request,
            ICommandHandler<UpdateStageTemplateCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new UpdateStageTemplateCommand(stageTemplateId, request.Name, request.Description, request.IsDefault), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Manage);

        templates.MapDelete("/{stageTemplateId:guid}", async (
            Guid stageTemplateId, ICommandHandler<DeleteStageTemplateCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new DeleteStageTemplateCommand(stageTemplateId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Manage);

        templates.MapGet("/{stageTemplateId:guid}/stages", async (
            Guid stageTemplateId, IQueryHandler<GetStagesByTemplateQuery, IReadOnlyList<StageResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<StageResponse>> result = await handler.HandleAsync(new GetStagesByTemplateQuery(stageTemplateId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Read);

        templates.MapPost("/{stageTemplateId:guid}/stages", async (
            Guid stageTemplateId, CreateStageRequest request,
            ICommandHandler<CreateStageCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(
                new CreateStageCommand(
                    stageTemplateId, request.Name, request.Order, request.WeightPercent, request.Description, request.DefaultFee), ct);
            return result.Match(id => Results.Ok(new { StageId = id }), r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Manage);

        RouteGroupBuilder stages = app.MapGroup("/api/stages").WithTags("Stages (template)");

        stages.MapPut("/{stageId:guid}", async (
            Guid stageId, UpdateStageRequest request, ICommandHandler<UpdateStageCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new UpdateStageCommand(
                    stageId, request.Name, request.Order, request.WeightPercent, request.Description, request.DefaultFee), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Manage);

        stages.MapDelete("/{stageId:guid}", async (
            Guid stageId, ICommandHandler<DeleteStageCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new DeleteStageCommand(stageId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Manage);

        stages.MapGet("/{stageId:guid}/tasks", async (
            Guid stageId, IQueryHandler<GetStageTasksByStageQuery, IReadOnlyList<StageTaskResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<StageTaskResponse>> result = await handler.HandleAsync(new GetStageTasksByStageQuery(stageId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Read);

        stages.MapPost("/{stageId:guid}/tasks", async (
            Guid stageId, CreateStageTaskRequest request,
            ICommandHandler<CreateStageTaskCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(new CreateStageTaskCommand(
                stageId, request.Name, request.Order, request.Weight, request.Description, request.DepartmentId), ct);
            return result.Match(id => Results.Ok(new { StageTaskId = id }), r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Manage);

        stages.MapGet("/{stageId:guid}/departments", async (
            Guid stageId, IQueryHandler<GetStageDepartmentsQuery, IReadOnlyList<StageDepartmentResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<StageDepartmentResponse>> result = await handler.HandleAsync(new GetStageDepartmentsQuery(stageId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Read);

        stages.MapPost("/{stageId:guid}/departments/{departmentId:guid}", async (
            Guid stageId, Guid departmentId, ICommandHandler<AddStageDepartmentCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new AddStageDepartmentCommand(stageId, departmentId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Manage);

        stages.MapDelete("/{stageId:guid}/departments/{departmentId:guid}", async (
            Guid stageId, Guid departmentId, ICommandHandler<RemoveStageDepartmentCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new RemoveStageDepartmentCommand(stageId, departmentId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Manage);

        RouteGroupBuilder stageTasks = app.MapGroup("/api/stage-tasks").WithTags("StageTasks (template)");

        stageTasks.MapPut("/{stageTaskId:guid}", async (
            Guid stageTaskId, UpdateStageTaskRequest request,
            ICommandHandler<UpdateStageTaskCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new UpdateStageTaskCommand(
                stageTaskId, request.Name, request.Order, request.Weight, request.Description, request.DepartmentId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Manage);

        stageTasks.MapDelete("/{stageTaskId:guid}", async (
            Guid stageTaskId, ICommandHandler<DeleteStageTaskCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new DeleteStageTaskCommand(stageTaskId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Manage);

        stageTasks.MapGet("/{stageTaskId:guid}/sub-tasks", async (
            Guid stageTaskId, IQueryHandler<GetStageSubTasksByStageTaskQuery, IReadOnlyList<StageSubTaskResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<StageSubTaskResponse>> result = await handler.HandleAsync(new GetStageSubTasksByStageTaskQuery(stageTaskId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Read);

        stageTasks.MapPost("/{stageTaskId:guid}/sub-tasks", async (
            Guid stageTaskId, CreateStageSubTaskRequest request,
            ICommandHandler<CreateStageSubTaskCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> result = await handler.HandleAsync(new CreateStageSubTaskCommand(
                stageTaskId, request.Title, request.Order, request.IsRequired, request.Description), ct);
            return result.Match(id => Results.Ok(new { StageSubTaskId = id }), r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Manage);

        RouteGroupBuilder stageSubTasks = app.MapGroup("/api/stage-sub-tasks").WithTags("StageSubTasks (template)");

        stageSubTasks.MapPut("/{stageSubTaskId:guid}", async (
            Guid stageSubTaskId, UpdateStageSubTaskRequest request,
            ICommandHandler<UpdateStageSubTaskCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new UpdateStageSubTaskCommand(
                stageSubTaskId, request.Title, request.Order, request.IsRequired, request.Description), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Manage);

        stageSubTasks.MapDelete("/{stageSubTaskId:guid}", async (
            Guid stageSubTaskId, ICommandHandler<DeleteStageSubTaskCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new DeleteStageSubTaskCommand(stageSubTaskId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Stages.Manage);
    }

    private sealed record UpdateStageTemplateRequest(string? Name, string? Description, bool? IsDefault);
    private sealed record CreateStageRequest(string Name, int Order, decimal WeightPercent, string? Description, decimal? DefaultFee);
    private sealed record UpdateStageRequest(
        string? Name, int? Order, decimal? WeightPercent, string? Description, decimal? DefaultFee);
    private sealed record CreateStageTaskRequest(string Name, int Order, decimal Weight, string? Description, Guid? DepartmentId);
    private sealed record UpdateStageTaskRequest(string? Name, int? Order, decimal? Weight, string? Description, Guid? DepartmentId);
    private sealed record CreateStageSubTaskRequest(string Title, int Order, bool IsRequired, string? Description);
    private sealed record UpdateStageSubTaskRequest(string? Title, int? Order, bool? IsRequired, string? Description);
}
