using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Crm.Contracts;
using Crm.Contracts.Briefs;
using Crm.Contracts.Items;
using Crm.Contracts.Meetings;
using Crm.Contracts.Portal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Crm.Infrastructure.Endpoints;

/// <summary>
/// The client-facing Portal. Every declared <c>Crm.Contracts.Portal</c> operation is mapped; the client
/// decides its own approvals here (B1, <see cref="DecideMyPortalApprovalCommand"/>, through
/// <c>Approvals.Contracts.IApprovalDecider</c>).
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
            Guid projectId, Guid? stageId, ICurrentUser currentUser, IQueryHandler<GetMyPortalBriefQuery, PortalBriefViewResponse> handler,
            CancellationToken ct) =>
        {
            Result<PortalBriefViewResponse> result = await handler.HandleAsync(new GetMyPortalBriefQuery(currentUser.UserId!, projectId, stageId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Briefs.ViewOwn);

        portal.MapGet("/briefs/{briefId:guid}/rounds", async (
            Guid briefId, ICurrentUser currentUser,
            IQueryHandler<GetMyPortalBriefRoundsQuery, IReadOnlyList<CrmBriefRoundResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<CrmBriefRoundResponse>> result = await handler.HandleAsync(
                new GetMyPortalBriefRoundsQuery(currentUser.UserId!, briefId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Briefs.ViewOwn);

        // crm-complete F2 (D5): the client's own profile. crm.portal. Read-only: the portal has no edit form.
        portal.MapGet("/profile", async (
            ICurrentUser currentUser, IQueryHandler<GetMyPortalProfileQuery, PortalProfileResponse> handler, CancellationToken ct) =>
        {
            Result<PortalProfileResponse> result = await handler.HandleAsync(new GetMyPortalProfileQuery(currentUser.UserId!), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Portal);

        // crm-complete F1 (D5): the client's own requirement items. requirements.view_own (the existing _own code).
        portal.MapGet("/requirement-items", async (
            Guid? projectId, Guid? periodId, CrmApprovalState? status, int? pageNumber, int? pageSize, ICurrentUser currentUser,
            IQueryHandler<GetMyPortalRequirementItemsQuery, PagedResult<PortalRequirementItemResponse>> handler, CancellationToken ct) =>
        {
            Result<PagedResult<PortalRequirementItemResponse>> result = await handler.HandleAsync(new GetMyPortalRequirementItemsQuery(
                currentUser.UserId!, projectId, periodId, status, pageNumber ?? 1, pageSize ?? 50), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Requirements.ViewOwn);

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

        // B1.3: the client's approvals inbox, paged on the Approvals side.
        portal.MapGet("/approvals", async (
            string? status, int? pageNumber, int? pageSize, ICurrentUser currentUser,
            IQueryHandler<GetMyPortalApprovalsQuery, PagedResult<PortalApprovalResponse>> handler, CancellationToken ct) =>
        {
            Result<PagedResult<PortalApprovalResponse>> result = await handler.HandleAsync(
                new GetMyPortalApprovalsQuery(currentUser.UserId!, status, pageNumber ?? 1, pageSize ?? 50), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Approvals.ViewOwn);

        // B1.2: the client's own decision. 403 (decide_own) → 404 (not the caller's subject) → 409 (decided).
        portal.MapPut("/approvals/{approvalId:guid}/decide", async (
            Guid approvalId, DecidePortalApprovalRequest request, ICurrentUser currentUser,
            ICommandHandler<DecideMyPortalApprovalCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(
                new DecideMyPortalApprovalCommand(currentUser.UserId!, approvalId, request.Decision, request.Comment), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Approvals.DecideOwn);

        portal.MapGet("/meetings", async (
            int? pageNumber, int? pageSize,
            ICurrentUser currentUser, IQueryHandler<GetMyPortalMeetingsQuery, PagedResult<PortalMeetingResponse>> handler,
            CancellationToken ct) =>
        {
            Result<PagedResult<PortalMeetingResponse>> result = await handler.HandleAsync(
                new GetMyPortalMeetingsQuery(currentUser.UserId!, pageNumber ?? 1, pageSize ?? 50), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Meetings.ViewOwn);

        // B-1 (H2/X-7): the portal-only meeting detail, with slots — never crm.meetings.read (MT-N1).
        portal.MapGet("/meetings/{meetingId:guid}", async (
            Guid meetingId, ICurrentUser currentUser,
            IQueryHandler<GetMyPortalMeetingByIdQuery, PortalMeetingDetailResponse> handler, CancellationToken ct) =>
        {
            Result<PortalMeetingDetailResponse> result = await handler.HandleAsync(
                new GetMyPortalMeetingByIdQuery(currentUser.UserId!, meetingId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Meetings.ViewOwn);

        // UI-1: the office's free slots for the client's own project, gated on the permission a client needs to
        // request one of them — never crm.meetings.read (MT-N1). Same query and response as /api/crm/meetings/free-slots.
        portal.MapGet("/meetings/free-slots", async (
            Guid projectId, DateTime fromUtc, DateTime toUtc, ICurrentUser currentUser,
            IQueryHandler<GetMyPortalFreeMeetingSlotsQuery, IReadOnlyList<FreeSlotResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<FreeSlotResponse>> result = await handler.HandleAsync(
                new GetMyPortalFreeMeetingSlotsQuery(currentUser.UserId!, projectId, fromUtc, toUtc), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Meetings.RequestOwn);

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
                id => Results.Created($"/api/crm/portal/meetings/{id}", new { MeetingId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Meetings.RequestOwn);

        portal.MapPut("/meetings/slots/{slotId:guid}/accept", async (
            Guid slotId, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<AcceptCrmMeetingSlotByContactCommand> handler, CancellationToken ct) =>
        {
            Result<Guid> contactId = await CrmEndpointActors.ResolveContactIdAsync(guard, currentUser.UserId, ct);
            if (contactId.IsFailure)
            {
                return contactId.ToProblem();
            }

            Result result = await handler.HandleAsync(new AcceptCrmMeetingSlotByContactCommand(slotId, contactId.Value), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Meetings.RespondOwn);

        portal.MapPut("/meetings/slots/{slotId:guid}/decline", async (
            Guid slotId, DeclineSlotRequest request, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<DeclineCrmMeetingSlotByContactCommand> handler, CancellationToken ct) =>
        {
            Result<Guid> contactId = await CrmEndpointActors.ResolveContactIdAsync(guard, currentUser.UserId, ct);
            if (contactId.IsFailure)
            {
                return contactId.ToProblem();
            }

            Result result = await handler.HandleAsync(
                new DeclineCrmMeetingSlotByContactCommand(slotId, contactId.Value, request.CounterSlots), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Meetings.RespondOwn);

        portal.MapPut("/meetings/{meetingId:guid}/reschedule", async (
            Guid meetingId, RescheduleMeetingRequest request, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<RescheduleCrmMeetingByContactCommand> handler, CancellationToken ct) =>
        {
            Result<Guid> contactId = await CrmEndpointActors.ResolveContactIdAsync(guard, currentUser.UserId, ct);
            if (contactId.IsFailure)
            {
                return contactId.ToProblem();
            }

            Result result = await handler.HandleAsync(new RescheduleCrmMeetingByContactCommand(
                meetingId, contactId.Value, request.StartsAtUtc, request.EndsAtUtc), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Meetings.RespondOwn);

        portal.MapPut("/meetings/{meetingId:guid}/cancel", async (
            Guid meetingId, CancelMeetingRequest request, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<CancelCrmMeetingByContactCommand> handler, CancellationToken ct) =>
        {
            Result<Guid> contactId = await CrmEndpointActors.ResolveContactIdAsync(guard, currentUser.UserId, ct);
            if (contactId.IsFailure)
            {
                return contactId.ToProblem();
            }

            Result result = await handler.HandleAsync(
                new CancelCrmMeetingByContactCommand(meetingId, contactId.Value, request.Reason), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Meetings.RespondOwn);
    }

    private sealed record DecidePortalApprovalRequest(PortalApprovalDecision Decision, string? Comment = null);

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
