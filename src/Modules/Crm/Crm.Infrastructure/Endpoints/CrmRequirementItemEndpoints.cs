using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Crm.Contracts;
using Crm.Contracts.Items;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Crm.Infrastructure.Endpoints;

/// <summary>Office-side requirement item CRUD + send + supersede, governed by
/// <see cref="CrmPermissions.Items"/>. <c>EmployeeId</c> fields are resolved from the caller via
/// <see cref="CrmEndpointActors.ResolveEmployeeIdAsync"/>. The client-authored creation path
/// (<c>CreateCrmRequirementItemByClientCommand</c>) lives on <see cref="CrmPortalEndpoints"/>, not
/// here — it resolves a contact actor, not an employee.</summary>
internal sealed class CrmRequirementItemEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder byPeriod = app.MapGroup("/api/crm/periods/{periodId:guid}/items").WithTags("Crm Requirement Items");

        byPeriod.MapPost("/", async (
            Guid periodId, CreateItemRequest request, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<CreateCrmRequirementItemCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> employeeId = await CrmEndpointActors.ResolveEmployeeIdAsync(guard, currentUser.UserId, ct);
            if (employeeId.IsFailure)
            {
                return employeeId.ToProblem();
            }

            Result<Guid> result = await handler.HandleAsync(new CreateCrmRequirementItemCommand(
                request.ProjectId, periodId, employeeId.Value, request.Category, request.Title, request.Priority,
                currentUser.UserId!, request.SpaceTypeId, request.Room, request.Description, request.SourceType, request.MeetingId,
                request.BriefId, request.NoteId, request.ProjectTaskId), ct);
            return result.Match(id => Results.Created($"/api/crm/requirement-items/{id}", new { ItemId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Items.Create);

        byPeriod.MapGet("/", async (
            Guid periodId, ICurrentUser currentUser,
            IQueryHandler<GetCrmRequirementItemsByPeriodQuery, IReadOnlyList<CrmRequirementItemResponse>> handler, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result<IReadOnlyList<CrmRequirementItemResponse>> result = await handler.HandleAsync(
                new GetCrmRequirementItemsByPeriodQuery(periodId, userId.Value!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Items.Read);

        app.MapGet("/api/crm/project-tasks/{taskId:guid}/requirement-items", async (
            Guid taskId, ICurrentUser currentUser,
            IQueryHandler<GetCrmRequirementItemsByTaskQuery, IReadOnlyList<CrmRequirementItemResponse>> handler, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result<IReadOnlyList<CrmRequirementItemResponse>> result = await handler.HandleAsync(
                new GetCrmRequirementItemsByTaskQuery(taskId, userId.Value!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).WithTags("Crm Requirement Items").RequirePermission(CrmPermissions.Items.Read);

        RouteGroupBuilder byId = app.MapGroup("/api/crm/requirement-items/{itemId:guid}").WithTags("Crm Requirement Items");

        byId.MapPut("/", async (
            Guid itemId, UpdateItemRequest request, ICurrentUser currentUser,
            ICommandHandler<UpdateCrmRequirementItemCommand> handler, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result result = await handler.HandleAsync(new UpdateCrmRequirementItemCommand(
                itemId, request.Category, request.Title, request.Priority, request.SpaceTypeId, request.Room,
                request.Description, request.SourceType, request.MeetingId, request.BriefId, request.NoteId,
                request.ProjectTaskId, userId.Value!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Items.Update);

        byId.MapPut("/send", async (
            Guid itemId, ICurrentUser currentUser, ICommandHandler<SendCrmRequirementItemCommand> handler, CancellationToken ct) =>
        {
            Result<string> sentBy = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (sentBy.IsFailure)
            {
                return sentBy.ToProblem();
            }

            Result result = await handler.HandleAsync(new SendCrmRequirementItemCommand(itemId, sentBy.Value!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Items.Send);

        byId.MapPost("/supersede", async (
            Guid itemId, SupersedeItemRequest request, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<SupersedeCrmRequirementItemCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> employeeId = await CrmEndpointActors.ResolveEmployeeIdAsync(guard, currentUser.UserId, ct);
            if (employeeId.IsFailure)
            {
                return employeeId.ToProblem();
            }

            Result<Guid> result = await handler.HandleAsync(new SupersedeCrmRequirementItemCommand(
                itemId, employeeId.Value, request.Category, request.Title, request.Priority, currentUser.UserId!, request.SpaceTypeId,
                request.Room, request.Description, request.SourceType, request.MeetingId, request.BriefId,
                request.NoteId, request.ProjectTaskId), ct);
            return result.Match(id => Results.Created($"/api/crm/requirement-items/{id}", new { ItemId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Items.Supersede);
    }

    private sealed record CreateItemRequest(
        Guid ProjectId, RequirementCategory Category, string Title, RequirementPriority Priority,
        Guid? SpaceTypeId = null, string? Room = null, string? Description = null,
        RequirementSource? SourceType = null, Guid? MeetingId = null, Guid? BriefId = null,
        Guid? NoteId = null, Guid? ProjectTaskId = null);

    private sealed record SupersedeItemRequest(
        RequirementCategory Category, string Title, RequirementPriority Priority,
        Guid? SpaceTypeId = null, string? Room = null, string? Description = null,
        RequirementSource? SourceType = null, Guid? MeetingId = null, Guid? BriefId = null,
        Guid? NoteId = null, Guid? ProjectTaskId = null);

    private sealed record UpdateItemRequest(
        RequirementCategory Category, string Title, RequirementPriority Priority, Guid? SpaceTypeId,
        string? Room, string? Description, RequirementSource? SourceType, Guid? MeetingId, Guid? BriefId,
        Guid? NoteId, Guid? ProjectTaskId);
}
