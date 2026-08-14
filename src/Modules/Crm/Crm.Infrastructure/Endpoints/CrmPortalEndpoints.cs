using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Crm.Contracts;
using Crm.Contracts.Items;
using Crm.Contracts.Portal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Crm.Infrastructure.Endpoints;

/// <summary>
/// The client-facing Portal — the only two <c>Crm.Contracts</c> Portal-reachable operations this phase
/// actually implements: <see cref="GetMyPortalBriefQuery"/> (own Application handler, G9-14) and the
/// client-authored requirement-item submission (<c>CreateCrmRequirementItemByClientCommand</c>, its own
/// handler built at G9-12 — its own doc comment already calls it "the only Portal write in this
/// sub-area"). <c>GetMyPortalMeetingsQuery</c> and <c>DecideMyPortalApprovalCommand</c> are deliberately
/// NOT mapped — no handler exists for either; see <c>GetMyPortalBriefQueryHandler</c>'s own remarks for
/// why closing that gap is deferred rather than improvised here.
///
/// <c>AppUserId</c>/<c>ContactId</c> are always resolved from the caller (<see cref="ICurrentUser"/> /
/// <see cref="CrmEndpointActors.ResolveContactIdAsync"/>) — a Portal caller names no one but themselves.
/// </summary>
internal sealed class CrmPortalEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var portal = app.MapGroup("/api/crm/portal").WithTags("Crm Portal");

        portal.MapGet("/projects/{projectId:guid}/brief", async (
            Guid projectId, ICurrentUser currentUser, IQueryHandler<GetMyPortalBriefQuery, PortalBriefResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetMyPortalBriefQuery(currentUser.UserId!, projectId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Briefs.ViewOwn);

        portal.MapPost("/requirement-items", async (
            AddPortalItemRequest request, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<CreateCrmRequirementItemByClientCommand, Guid> handler, CancellationToken ct) =>
        {
            var contactId = await CrmEndpointActors.ResolveContactIdAsync(guard, currentUser.UserId, ct);
            if (contactId.IsFailure)
                return contactId.ToProblem();

            var result = await handler.HandleAsync(new CreateCrmRequirementItemByClientCommand(
                request.ProjectId, request.PeriodId, contactId.Value, request.Category, request.Title,
                request.Priority, request.SpaceTypeId, request.Room, request.Description), ct);
            return result.Match(
                id => Results.Created($"/api/crm/requirement-items/{id}", new { ItemId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Requirements.SubmitOwn);
    }

    private sealed record AddPortalItemRequest(
        Guid ProjectId, Guid PeriodId, RequirementCategory Category, string Title, RequirementPriority Priority,
        Guid? SpaceTypeId = null, string? Room = null, string? Description = null);
}
