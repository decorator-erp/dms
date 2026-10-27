using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Crm.Contracts;
using Crm.Contracts.Briefs;
using Crm.Contracts.Portal;
using Crm.Infrastructure.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Crm.Infrastructure.Endpoints;

/// <summary>Design brief CRUD + send, and brief-space entry CRUD + send/supersede, governed by
/// <see cref="CrmPermissions.Briefs"/>. <c>CreatedById</c> fields are resolved from the caller via
/// <see cref="CrmEndpointActors.ResolveEmployeeIdAsync"/> — never client-supplied. Every message here is
/// <see cref="ICrmProjectScoped"/>: its <c>UserId</c> is the caller's, and <c>CrmProjectScopeDecorator</c>
/// 404s a caller who cannot reach the subject's project.</summary>
internal sealed class CrmBriefEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder byProject = app.MapGroup("/api/crm/projects/{projectId:guid}/brief").WithTags("Crm Briefs");

        byProject.MapPost("/", async (
            Guid projectId, CreateBriefRequest request, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<CreateCrmDesignBriefCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result<Guid> createdById = await CrmEndpointActors.ResolveEmployeeIdAsync(guard, currentUser.UserId, ct);
            if (createdById.IsFailure)
            {
                return createdById.ToProblem();
            }

            Result<Guid> result = await handler.HandleAsync(
                new CreateCrmDesignBriefCommand(projectId, createdById.Value, request.ProjectStageId, request.Data, userId.Value!), ct);
            // The brief is read back by its stage - there is no GET /api/crm/briefs/{id}.
            return result.Match(
                id => Results.Created($"/api/crm/stages/{request.ProjectStageId}/brief", new { BriefId = id }),
                r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Briefs.Manage);

        // Obsolete: one brief per stage - returns the project's latest. Prefer GET /api/crm/stages/{stageId}/brief.
        byProject.MapGet("/", async (
            Guid projectId, ICurrentUser currentUser,
            IQueryHandler<GetCrmBriefByProjectQuery, CrmDesignBriefResponse> handler, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result<CrmDesignBriefResponse> result = await handler.HandleAsync(new GetCrmBriefByProjectQuery(projectId, userId.Value!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Briefs.Read);

        app.MapGet("/api/crm/stages/{stageId:guid}/brief", async (
            Guid stageId, ICurrentUser currentUser,
            IQueryHandler<GetCrmBriefByStageQuery, CrmDesignBriefResponse> handler, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result<CrmDesignBriefResponse> result = await handler.HandleAsync(new GetCrmBriefByStageQuery(stageId, userId.Value!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).WithTags("Crm Briefs").RequirePermission(CrmPermissions.Briefs.Read);

        RouteGroupBuilder byId = app.MapGroup("/api/crm/briefs/{briefId:guid}").WithTags("Crm Briefs");

        byId.MapPut("/head", async (
            Guid briefId, UpdateHeadRequest request, ICurrentUser currentUser,
            ICommandHandler<UpdateCrmBriefHeadCommand> handler, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result result = await handler.HandleAsync(
                new UpdateCrmBriefHeadCommand(briefId, request.Data, userId.Value!, request.LastMeetingId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Briefs.Manage);

        byId.MapPut("/send", async (
            Guid briefId, ICurrentUser currentUser, ICommandHandler<SendCrmBriefCommand> handler, CancellationToken ct) =>
        {
            Result<string> sentBy = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (sentBy.IsFailure)
            {
                return sentBy.ToProblem();
            }

            Result result = await handler.HandleAsync(new SendCrmBriefCommand(briefId, sentBy.Value!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Briefs.Send);

        byId.MapPost("/spaces", async (
            Guid briefId, BriefSpaceRequest request, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<CreateCrmBriefSpaceCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result<Guid> createdById = await CrmEndpointActors.ResolveEmployeeIdAsync(guard, currentUser.UserId, ct);
            if (createdById.IsFailure)
            {
                return createdById.ToProblem();
            }

            Result<Guid> result = await handler.HandleAsync(new CreateCrmBriefSpaceCommand(
                briefId, createdById.Value, userId.Value!, request.SpaceId, request.CountOrPieces, request.Requirements,
                request.MeetingId, request.ProjectTaskId), ct);
            return result.Match(id => Results.Created($"/api/crm/brief-spaces/{id}", new { BriefSpaceId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Briefs.Manage);

        byId.MapGet("/spaces", async (
            Guid briefId, ICurrentUser currentUser,
            IQueryHandler<GetCrmBriefSpacesByBriefQuery, IReadOnlyList<CrmBriefSpaceResponse>> handler, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result<IReadOnlyList<CrmBriefSpaceResponse>> result = await handler.HandleAsync(
                new GetCrmBriefSpacesByBriefQuery(briefId, userId.Value!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Briefs.Read);

        // FR-029 round diff, two audiences on one route. Route claims decide the audience: crm.briefs.read
        // is the office (scoped by CrmProjectScopeDecorator, budget included); otherwise
        // crm.briefs.view_own is the client (own projects only, no budget); neither is a 403. There is no
        // any-of permission helper, hence RequireAuthorization() plus the explicit checks.
        byId.MapGet("/rounds/{round:int}/diff", async (
            Guid briefId, int round, ICurrentUser currentUser,
            IQueryHandler<GetCrmBriefRoundDiffQuery, BriefHeadDiffResponse> officeHandler,
            IQueryHandler<GetMyPortalBriefRoundDiffQuery, BriefHeadDiffResponse> clientHandler, CancellationToken ct) =>
        {
            if (currentUser.UserId is not { } userId)
            {
                return Result.Failure(CrmAccessErrors.Unauthenticated).ToProblem();
            }

            Result<BriefHeadDiffResponse> result;
            if (currentUser.HasPermission(CrmPermissions.Briefs.Read))
            {
                result = await officeHandler.HandleAsync(new GetCrmBriefRoundDiffQuery(briefId, round, userId), ct);
            }
            else if (currentUser.HasPermission(CrmPermissions.Briefs.ViewOwn))
            {
                result = await clientHandler.HandleAsync(new GetMyPortalBriefRoundDiffQuery(userId, briefId, round), ct);
            }
            else
            {
                return Results.Forbid();
            }

            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequireAuthorization();

        // The office's list of the brief's rounds (voided ones included); the client's is the portal route.
        byId.MapGet("/rounds", async (
            Guid briefId, ICurrentUser currentUser,
            IQueryHandler<GetCrmBriefRoundsQuery, IReadOnlyList<CrmBriefRoundResponse>> handler, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result<IReadOnlyList<CrmBriefRoundResponse>> result = await handler.HandleAsync(
                new GetCrmBriefRoundsQuery(briefId, userId.Value!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Briefs.Read);

        RouteGroupBuilder spaces = app.MapGroup("/api/crm/brief-spaces/{entryId:guid}").WithTags("Crm Briefs");

        spaces.MapPut("/", async (
            Guid entryId, UpdateBriefSpaceRequest request, ICurrentUser currentUser,
            ICommandHandler<UpdateCrmBriefSpaceCommand> handler, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result result = await handler.HandleAsync(new UpdateCrmBriefSpaceCommand(
                entryId, userId.Value!, request.SpaceId, request.CountOrPieces, request.Requirements,
                request.MeetingId, request.ProjectTaskId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Briefs.Manage);

        spaces.MapPut("/send", async (
            Guid entryId, ICurrentUser currentUser, ICommandHandler<SendCrmBriefSpaceCommand> handler, CancellationToken ct) =>
        {
            Result<string> sentBy = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (sentBy.IsFailure)
            {
                return sentBy.ToProblem();
            }

            Result result = await handler.HandleAsync(new SendCrmBriefSpaceCommand(entryId, sentBy.Value!), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Briefs.Send);

        spaces.MapPost("/supersede", async (
            Guid entryId, BriefSpaceRequest request, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<SupersedeCrmBriefSpaceCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result<Guid> createdById = await CrmEndpointActors.ResolveEmployeeIdAsync(guard, currentUser.UserId, ct);
            if (createdById.IsFailure)
            {
                return createdById.ToProblem();
            }

            Result<Guid> result = await handler.HandleAsync(new SupersedeCrmBriefSpaceCommand(
                entryId, createdById.Value, userId.Value!, request.SpaceId, request.CountOrPieces, request.Requirements,
                request.MeetingId, request.ProjectTaskId), ct);
            return result.Match(id => Results.Created($"/api/crm/brief-spaces/{id}", new { BriefSpaceId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Briefs.Manage);
    }

    private sealed record CreateBriefRequest(CrmBriefHeadData Data, Guid ProjectStageId);
    private sealed record UpdateHeadRequest(CrmBriefHeadData Data, Guid? LastMeetingId = null);

    private sealed record BriefSpaceRequest(
        Guid? SpaceId, int? CountOrPieces = null, string? Requirements = null,
        Guid? MeetingId = null, Guid? ProjectTaskId = null);

    private sealed record UpdateBriefSpaceRequest(
        Guid? SpaceId, int? CountOrPieces, string? Requirements, Guid? MeetingId, Guid? ProjectTaskId);
}
