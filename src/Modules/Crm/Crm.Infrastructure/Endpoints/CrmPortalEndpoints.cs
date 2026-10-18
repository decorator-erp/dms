using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Crm.Contracts;
using Crm.Contracts.Items;
using Crm.Contracts.Meetings;
using Crm.Contracts.Portal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Crm.Infrastructure.Endpoints;

/// <summary>
/// The client-facing Portal. As of M1-02, every declared <c>Crm.Contracts.Portal</c> operation except
/// <c>DecideMyPortalApprovalCommand</c> is mapped: <see cref="GetMyPortalBriefQuery"/> and the
/// client-authored requirement-item submission (G9-14), plus <see cref="GetMyPortalMeetingsQuery"/> and
/// the five client-side meeting operations (M1-02). <c>DecideMyPortalApprovalCommand</c> stays
/// unmapped — see <c>GetMyPortalBriefQueryHandler</c>'s own remarks and the Scope-boundaries table for
/// why closing that one is Approvals' call, not this file's.
///
/// <c>AppUserId</c>/<c>ContactId</c> are always resolved from the caller (<see cref="ICurrentUser"/> /
/// <see cref="CrmEndpointActors.ResolveContactIdAsync"/>) — a Portal caller names no one but themselves.
/// Every meeting route here resolves a CONTACT actor; the office mirror of each lives in
/// <see cref="CrmMeetingEndpoints"/>, resolving an EMPLOYEE instead.
/// </summary>
internal sealed class CrmPortalEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder portal = app.MapGroup("/api/crm/portal").WithTags("Crm Portal");

        portal.MapGet("/projects/{projectId:guid}/brief", async (
            Guid projectId, Guid? stageId, ICurrentUser currentUser, IQueryHandler<GetMyPortalBriefQuery, PortalBriefResponse> handler,
            CancellationToken ct) =>
        {
            Result<PortalBriefResponse> result = await handler.HandleAsync(new GetMyPortalBriefQuery(currentUser.UserId!, projectId, stageId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Briefs.ViewOwn);

        portal.MapPost("/requirement-items", async (
            AddPortalItemRequest request, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<CreateCrmRequirementItemByClientCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> contactId = await CrmEndpointActors.ResolveContactIdAsync(guard, currentUser.UserId, ct);
            if (contactId.IsFailure)
            {
                return contactId.ToProblem();
            }

            Result<Guid> result = await handler.HandleAsync(new CreateCrmRequirementItemByClientCommand(
                request.ProjectId, request.PeriodId, contactId.Value, request.Category, request.Title,
                request.Priority, request.SpaceTypeId, request.Room, request.Description), ct);
            return result.Match(
                id => Results.Created($"/api/crm/requirement-items/{id}", new { ItemId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Requirements.SubmitOwn);

        portal.MapGet("/meetings", async (
            ICurrentUser currentUser, IQueryHandler<GetMyPortalMeetingsQuery, IReadOnlyList<PortalMeetingResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyList<PortalMeetingResponse>> result = await handler.HandleAsync(new GetMyPortalMeetingsQuery(currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Meetings.ViewOwn);

        portal.MapPost("/meetings/request", async (
            RequestMeetingRequest request, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<RequestCrmMeetingByClientCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> contactId = await CrmEndpointActors.ResolveContactIdAsync(guard, currentUser.UserId, ct);
            if (contactId.IsFailure)
            {
                return contactId.ToProblem();
            }

            Result<Guid> result = await handler.HandleAsync(new RequestCrmMeetingByClientCommand(
                request.ProjectId, contactId.Value, request.Title, request.Type,
                request.StartsAtUtc, request.EndsAtUtc, request.PeriodId, request.Location), ct);
            return result.Match(
                id => Results.Created($"/api/crm/meetings/{id}", new { MeetingId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Meetings.RequestOwn);

        portal.MapPut("/meetings/slots/{slotId:guid}/accept", async (
            Guid slotId, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<AcceptCrmMeetingSlotCommand> handler, CancellationToken ct) =>
        {
            Result<Guid> contactId = await CrmEndpointActors.ResolveContactIdAsync(guard, currentUser.UserId, ct);
            if (contactId.IsFailure)
            {
                return contactId.ToProblem();
            }

            Result result = await handler.HandleAsync(new AcceptCrmMeetingSlotCommand(slotId, null, contactId.Value), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Meetings.RespondOwn);

        portal.MapPut("/meetings/slots/{slotId:guid}/decline", async (
            Guid slotId, DeclineSlotRequest request, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<DeclineCrmMeetingSlotCommand> handler, CancellationToken ct) =>
        {
            Result<Guid> contactId = await CrmEndpointActors.ResolveContactIdAsync(guard, currentUser.UserId, ct);
            if (contactId.IsFailure)
            {
                return contactId.ToProblem();
            }

            Result result = await handler.HandleAsync(
                new DeclineCrmMeetingSlotCommand(slotId, null, contactId.Value, request.CounterSlots), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Meetings.RespondOwn);

        portal.MapPut("/meetings/{meetingId:guid}/reschedule", async (
            Guid meetingId, RescheduleMeetingRequest request, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<RescheduleCrmMeetingCommand> handler, CancellationToken ct) =>
        {
            Result<Guid> contactId = await CrmEndpointActors.ResolveContactIdAsync(guard, currentUser.UserId, ct);
            if (contactId.IsFailure)
            {
                return contactId.ToProblem();
            }

            Result result = await handler.HandleAsync(new RescheduleCrmMeetingCommand(
                meetingId, null, contactId.Value, request.StartsAtUtc, request.EndsAtUtc), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Meetings.RespondOwn);

        portal.MapPut("/meetings/{meetingId:guid}/cancel", async (
            Guid meetingId, CancelMeetingRequest request, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<CancelCrmMeetingCommand> handler, CancellationToken ct) =>
        {
            Result<Guid> contactId = await CrmEndpointActors.ResolveContactIdAsync(guard, currentUser.UserId, ct);
            if (contactId.IsFailure)
            {
                return contactId.ToProblem();
            }

            Result result = await handler.HandleAsync(
                new CancelCrmMeetingCommand(meetingId, null, contactId.Value, request.Reason), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Meetings.RespondOwn);
    }

    private sealed record AddPortalItemRequest(
        Guid ProjectId, Guid PeriodId, RequirementCategory Category, string Title, RequirementPriority Priority,
        Guid? SpaceTypeId = null, string? Room = null, string? Description = null);

    private sealed record RequestMeetingRequest(
        Guid ProjectId, string Title, MeetingType Type, DateTime StartsAtUtc, DateTime EndsAtUtc,
        Guid? PeriodId = null, string? Location = null);

    private sealed record DeclineSlotRequest(IReadOnlyList<CrmMeetingSlotRequest>? CounterSlots = null);
    private sealed record RescheduleMeetingRequest(DateTime StartsAtUtc, DateTime EndsAtUtc);
    private sealed record CancelMeetingRequest(string Reason);
}
