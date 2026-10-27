using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Projects.Contracts;
using Projects.Contracts.Scope;
using SharedKernel;

namespace Projects.Infrastructure.Endpoints;

/// <summary>The project's scope of work (نطاق العمل), office only: the client role holds none of the
/// <see cref="ProjectsPermissions.Scope"/> permissions (403). Every message is <see cref="IProjectScoped"/>,
/// so a project the caller can't reach is a 404. <see cref="IProjectScoped.UserId"/> comes from
/// <see cref="ICurrentUser"/>, never from the body.</summary>
internal sealed class ScopeOfWorkEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder scope = app.MapGroup("/api/projects/{projectId:guid}/scope-of-work").WithTags("Scope of work");

        scope.MapGet("/", async (
            Guid projectId, ICurrentUser currentUser,
            IQueryHandler<GetScopeOfWorkQuery, ScopeOfWorkResponse> handler, CancellationToken ct) =>
        {
            Result<ScopeOfWorkResponse> result = await handler.HandleAsync(new GetScopeOfWorkQuery(projectId, currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Scope.Read);

        scope.MapPut("/", async (
            Guid projectId, UpdateHeaderRequest request, ICurrentUser currentUser,
            ICommandHandler<UpdateScopeOfWorkHeaderCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new UpdateScopeOfWorkHeaderCommand(
                projectId, currentUser.UserId!, request.DurationDays, request.PlannedStartDate, request.AdjustmentAmount,
                request.AdjustmentNote, request.Notes), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Scope.Manage);

        scope.MapPost("/items", async (
            Guid projectId, AddItemsRequest request, ICurrentUser currentUser,
            ICommandHandler<AddScopeOfWorkItemsCommand, IReadOnlyList<ScopeOfWorkItemResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<ScopeOfWorkItemResponse>> result = await handler.HandleAsync(
                new AddScopeOfWorkItemsCommand(projectId, currentUser.UserId!, request.SourceStageIds ?? []), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Scope.Manage);

        scope.MapPost("/items/from-template/{stageTemplateId:guid}", async (
            Guid projectId, Guid stageTemplateId, ICurrentUser currentUser,
            ICommandHandler<AddScopeOfWorkTemplateCommand, IReadOnlyList<ScopeOfWorkItemResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<ScopeOfWorkItemResponse>> result = await handler.HandleAsync(
                new AddScopeOfWorkTemplateCommand(projectId, currentUser.UserId!, stageTemplateId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Scope.Manage);

        scope.MapPut("/items/order", async (
            Guid projectId, ReorderRequest request, ICurrentUser currentUser,
            ICommandHandler<ReorderScopeOfWorkItemsCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new ReorderScopeOfWorkItemsCommand(projectId, currentUser.UserId!, request.ItemIds ?? []), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Scope.Manage);

        scope.MapPut("/items/{itemId:guid}", async (
            Guid projectId, Guid itemId, UpdateItemRequest request, ICurrentUser currentUser,
            ICommandHandler<UpdateScopeOfWorkItemCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new UpdateScopeOfWorkItemCommand(
                projectId, currentUser.UserId!, itemId, request.Name ?? string.Empty, request.Description, request.Fee), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Scope.Manage);

        scope.MapDelete("/items/{itemId:guid}", async (
            Guid projectId, Guid itemId, ICurrentUser currentUser,
            ICommandHandler<RemoveScopeOfWorkItemCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new RemoveScopeOfWorkItemCommand(projectId, currentUser.UserId!, itemId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(ProjectsPermissions.Scope.Manage);
    }

    private sealed record UpdateHeaderRequest(
        int? DurationDays, DateOnly? PlannedStartDate, decimal AdjustmentAmount, string? AdjustmentNote, string? Notes);

    private sealed record AddItemsRequest(IReadOnlyList<Guid>? SourceStageIds);

    private sealed record UpdateItemRequest(string? Name, string? Description, decimal Fee);

    private sealed record ReorderRequest(IReadOnlyList<Guid>? ItemIds);
}
