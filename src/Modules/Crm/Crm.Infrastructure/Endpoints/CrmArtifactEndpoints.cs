using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Crm.Contracts;
using Crm.Contracts.Artifacts;
using Crm.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Crm.Infrastructure.Endpoints;

/// <summary>Artifact-document linking (a <c>documents.project_documents</c> row attached to a
/// brief/space/requirement-item/meeting), governed by <see cref="CrmPermissions.Artifacts"/>. No unlink
/// route — <c>Crm.Contracts</c> declares no <c>UnlinkCrmArtifactDocumentCommand</c> (G9-12's own
/// vertical-slice scope).</summary>
internal sealed class CrmArtifactEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder artifacts = app.MapGroup("/api/crm/artifacts").WithTags("Crm Artifacts");

        artifacts.MapPost("/", async (
            LinkArtifactRequest request, ICurrentUser currentUser, ICommandHandler<LinkCrmArtifactDocumentCommand, Guid> handler,
            CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            // FINDING-P1-16: the subject count first — the project scope can only resolve exactly one subject.
            if (new[] { request.BriefId, request.SpaceId, request.RequirementItemId, request.MeetingId }.Count(id => id is not null) != 1)
            {
                return Result.Failure(CrmArtifactErrors.InvalidSubject).ToProblem();
            }

            Result<Guid> result = await handler.HandleAsync(new LinkCrmArtifactDocumentCommand(
                request.DocumentId, request.BriefId, request.SpaceId, request.RequirementItemId, request.MeetingId,
                request.AddedByEmployeeId, request.AddedByContactId, userId.Value!), ct);
            return result.Match(id => Results.Created($"/api/crm/artifacts/{id}", new { ArtifactDocumentId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Artifacts.Manage);

        artifacts.MapGet("/by-brief/{briefId:guid}", async (
            Guid briefId, IQueryHandler<GetCrmArtifactDocumentsByBriefQuery, IReadOnlyList<CrmArtifactDocumentResponse>> handler,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result<IReadOnlyList<CrmArtifactDocumentResponse>> result = await handler.HandleAsync(new GetCrmArtifactDocumentsByBriefQuery(briefId, userId.Value!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Artifacts.Read);

        artifacts.MapGet("/by-space/{spaceId:guid}", async (
            Guid spaceId, IQueryHandler<GetCrmArtifactDocumentsBySpaceQuery, IReadOnlyList<CrmArtifactDocumentResponse>> handler,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result<IReadOnlyList<CrmArtifactDocumentResponse>> result = await handler.HandleAsync(new GetCrmArtifactDocumentsBySpaceQuery(spaceId, userId.Value!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Artifacts.Read);

        artifacts.MapGet("/by-requirement-item/{requirementItemId:guid}", async (
            Guid requirementItemId,
            IQueryHandler<GetCrmArtifactDocumentsByRequirementItemQuery, IReadOnlyList<CrmArtifactDocumentResponse>> handler,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result<IReadOnlyList<CrmArtifactDocumentResponse>> result = await handler.HandleAsync(new GetCrmArtifactDocumentsByRequirementItemQuery(requirementItemId, userId.Value!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Artifacts.Read);

        artifacts.MapGet("/by-meeting/{meetingId:guid}", async (
            Guid meetingId, IQueryHandler<GetCrmArtifactDocumentsByMeetingQuery, IReadOnlyList<CrmArtifactDocumentResponse>> handler,
            ICurrentUser currentUser, CancellationToken ct) =>
        {
            Result<string> userId = CrmEndpointActors.RequireUserId(currentUser.UserId);
            if (userId.IsFailure)
            {
                return userId.ToProblem();
            }

            Result<IReadOnlyList<CrmArtifactDocumentResponse>> result = await handler.HandleAsync(new GetCrmArtifactDocumentsByMeetingQuery(meetingId, userId.Value!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Artifacts.Read);
    }

    /// <summary>The link body: the command's fields without <c>UserId</c>, which comes from the token.</summary>
    private sealed record LinkArtifactRequest(
        Guid DocumentId, Guid? BriefId, Guid? SpaceId, Guid? RequirementItemId, Guid? MeetingId,
        Guid? AddedByEmployeeId, Guid? AddedByContactId);
}
