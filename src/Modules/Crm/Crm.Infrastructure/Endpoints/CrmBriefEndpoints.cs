using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Crm.Contracts;
using Crm.Contracts.Briefs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Crm.Infrastructure.Endpoints;

/// <summary>Design brief CRUD + send, and brief-space entry CRUD + send/supersede, governed by
/// <see cref="CrmPermissions.Briefs"/>. <c>CreatedById</c> fields are resolved from the caller via
/// <see cref="CrmEndpointActors.ResolveEmployeeIdAsync"/> — never client-supplied.</summary>
internal sealed class CrmBriefEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var byProject = app.MapGroup("/api/crm/projects/{projectId:guid}/brief").WithTags("Crm Briefs");

        byProject.MapPost("/", async (
            Guid projectId, CreateBriefRequest request, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<CreateCrmDesignBriefCommand, Guid> handler, CancellationToken ct) =>
        {
            var createdById = await CrmEndpointActors.ResolveEmployeeIdAsync(guard, currentUser.UserId, ct);
            if (createdById.IsFailure)
                return createdById.ToProblem();

            var result = await handler.HandleAsync(
                new CreateCrmDesignBriefCommand(projectId, createdById.Value, request.ProjectStageId, request.Data), ct);
            return result.Match(id => Results.Created($"/api/crm/briefs/{id}", new { BriefId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Briefs.Manage);

        byProject.MapGet("/", async (
            Guid projectId, IQueryHandler<GetCrmBriefByProjectQuery, CrmDesignBriefResponse> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetCrmBriefByProjectQuery(projectId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Briefs.Read);

        var byId = app.MapGroup("/api/crm/briefs/{briefId:guid}").WithTags("Crm Briefs");

        byId.MapPut("/head", async (
            Guid briefId, UpdateHeadRequest request, ICommandHandler<UpdateCrmBriefHeadCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new UpdateCrmBriefHeadCommand(briefId, request.Data, request.LastMeetingId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Briefs.Manage);

        byId.MapPut("/send", async (
            Guid briefId, ICommandHandler<SendCrmBriefCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new SendCrmBriefCommand(briefId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Briefs.Send);

        byId.MapPost("/spaces", async (
            Guid briefId, BriefSpaceRequest request, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<CreateCrmBriefSpaceCommand, Guid> handler, CancellationToken ct) =>
        {
            var createdById = await CrmEndpointActors.ResolveEmployeeIdAsync(guard, currentUser.UserId, ct);
            if (createdById.IsFailure)
                return createdById.ToProblem();

            var result = await handler.HandleAsync(new CreateCrmBriefSpaceCommand(
                briefId, createdById.Value, request.SpaceId, request.CountOrPieces, request.Requirements,
                request.MeetingId, request.ProjectTaskId), ct);
            return result.Match(id => Results.Created($"/api/crm/brief-spaces/{id}", new { BriefSpaceId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Briefs.Manage);

        byId.MapGet("/spaces", async (
            Guid briefId, IQueryHandler<GetCrmBriefSpacesByBriefQuery, IReadOnlyList<CrmBriefSpaceResponse>> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetCrmBriefSpacesByBriefQuery(briefId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Briefs.Read);

        var spaces = app.MapGroup("/api/crm/brief-spaces/{entryId:guid}").WithTags("Crm Briefs");

        spaces.MapPut("/", async (
            Guid entryId, UpdateBriefSpaceRequest request, ICommandHandler<UpdateCrmBriefSpaceCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new UpdateCrmBriefSpaceCommand(
                entryId, request.SpaceId, request.CountOrPieces, request.Requirements, request.MeetingId, request.ProjectTaskId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Briefs.Manage);

        spaces.MapPut("/send", async (
            Guid entryId, ICommandHandler<SendCrmBriefSpaceCommand> handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new SendCrmBriefSpaceCommand(entryId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Briefs.Send);

        spaces.MapPost("/supersede", async (
            Guid entryId, BriefSpaceRequest request, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<SupersedeCrmBriefSpaceCommand, Guid> handler, CancellationToken ct) =>
        {
            var createdById = await CrmEndpointActors.ResolveEmployeeIdAsync(guard, currentUser.UserId, ct);
            if (createdById.IsFailure)
                return createdById.ToProblem();

            var result = await handler.HandleAsync(new SupersedeCrmBriefSpaceCommand(
                entryId, createdById.Value, request.SpaceId, request.CountOrPieces, request.Requirements,
                request.MeetingId, request.ProjectTaskId), ct);
            return result.Match(id => Results.Created($"/api/crm/brief-spaces/{id}", new { BriefSpaceId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Briefs.Manage);
    }

    private sealed record CreateBriefRequest(CrmBriefHeadData Data, Guid? ProjectStageId = null);
    private sealed record UpdateHeadRequest(CrmBriefHeadData Data, Guid? LastMeetingId = null);

    private sealed record BriefSpaceRequest(
        Guid? SpaceId, int? CountOrPieces = null, string? Requirements = null,
        Guid? MeetingId = null, Guid? ProjectTaskId = null);

    private sealed record UpdateBriefSpaceRequest(
        Guid? SpaceId, int? CountOrPieces, string? Requirements, Guid? MeetingId, Guid? ProjectTaskId);
}
