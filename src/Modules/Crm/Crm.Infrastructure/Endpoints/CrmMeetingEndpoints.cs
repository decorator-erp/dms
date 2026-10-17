using BuildingBlocks;
using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Authorization;
using BuildingBlocks.Web.Results;
using Crm.Contracts;
using Crm.Contracts.Meetings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel;

namespace Crm.Infrastructure.Endpoints;

/// <summary>
/// M1-02: the office side of the meeting/slot state machine, governed by
/// <see cref="CrmPermissions.Meetings"/>. Every accept/decline/reschedule/cancel route here resolves
/// its caller as an EMPLOYEE via <see cref="CrmEndpointActors.ResolveEmployeeIdAsync"/> — the portal
/// mirror of each (client-side accept/decline/reschedule/cancel/request) lives in
/// <see cref="CrmPortalEndpoints"/>, resolving a CONTACT instead, matching the backend's own split
/// across two files. Which route succeeds for a given slot follows from
/// <c>MeetingSupport.IsCounterpartyKind</c>: an employee can only counter a contact-proposed slot, and
/// vice versa — so a client-requested meeting is accepted through this file, and an employee-proposed
/// one through the portal.
/// </summary>
internal sealed class CrmMeetingEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder meetings = app.MapGroup("/api/crm/meetings").WithTags("Crm Meetings");

        meetings.MapPost("/propose", async (
            ProposeMeetingRequest request, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<ProposeCrmMeetingCommand, Guid> handler, CancellationToken ct) =>
        {
            Result<Guid> employeeId = await CrmEndpointActors.ResolveEmployeeIdAsync(guard, currentUser.UserId, ct);
            if (employeeId.IsFailure)
            {
                return employeeId.ToProblem();
            }

            Result<Guid> result = await handler.HandleAsync(new ProposeCrmMeetingCommand(
                request.ProjectId, employeeId.Value, request.Title, request.Type, request.Slots,
                request.PeriodId, request.Location, request.DurationMinutes), ct);
            return result.Match(
                id => Results.Created($"/api/crm/meetings/{id}", new { MeetingId = id }), r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Meetings.Schedule);

        meetings.MapPut("/slots/{slotId:guid}/accept", async (
            Guid slotId, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<AcceptCrmMeetingSlotCommand> handler, CancellationToken ct) =>
        {
            Result<Guid> employeeId = await CrmEndpointActors.ResolveEmployeeIdAsync(guard, currentUser.UserId, ct);
            if (employeeId.IsFailure)
            {
                return employeeId.ToProblem();
            }

            Result result = await handler.HandleAsync(new AcceptCrmMeetingSlotCommand(slotId, employeeId.Value, null), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Meetings.RespondSlot);

        meetings.MapPut("/slots/{slotId:guid}/decline", async (
            Guid slotId, DeclineSlotRequest request, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<DeclineCrmMeetingSlotCommand> handler, CancellationToken ct) =>
        {
            Result<Guid> employeeId = await CrmEndpointActors.ResolveEmployeeIdAsync(guard, currentUser.UserId, ct);
            if (employeeId.IsFailure)
            {
                return employeeId.ToProblem();
            }

            Result result = await handler.HandleAsync(
                new DeclineCrmMeetingSlotCommand(slotId, employeeId.Value, null, request.CounterSlots), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Meetings.RespondSlot);

        meetings.MapPut("/{meetingId:guid}/reschedule", async (
            Guid meetingId, RescheduleMeetingRequest request, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<RescheduleCrmMeetingCommand> handler, CancellationToken ct) =>
        {
            Result<Guid> employeeId = await CrmEndpointActors.ResolveEmployeeIdAsync(guard, currentUser.UserId, ct);
            if (employeeId.IsFailure)
            {
                return employeeId.ToProblem();
            }

            Result result = await handler.HandleAsync(new RescheduleCrmMeetingCommand(
                meetingId, employeeId.Value, null, request.StartsAtUtc, request.EndsAtUtc), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Meetings.Reschedule);

        meetings.MapPut("/{meetingId:guid}/cancel", async (
            Guid meetingId, CancelMeetingRequest request, ICurrentUser currentUser, ICrmAccessGuard guard,
            ICommandHandler<CancelCrmMeetingCommand> handler, CancellationToken ct) =>
        {
            Result<Guid> employeeId = await CrmEndpointActors.ResolveEmployeeIdAsync(guard, currentUser.UserId, ct);
            if (employeeId.IsFailure)
            {
                return employeeId.ToProblem();
            }

            Result result = await handler.HandleAsync(
                new CancelCrmMeetingCommand(meetingId, employeeId.Value, null, request.Reason), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Meetings.Cancel);

        meetings.MapPut("/{meetingId:guid}/complete", async (
            Guid meetingId, ICommandHandler<CompleteCrmMeetingCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new CompleteCrmMeetingCommand(meetingId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Meetings.Complete);

        meetings.MapPut("/{meetingId:guid}/no-show", async (
            Guid meetingId, ICommandHandler<MarkCrmMeetingNoShowCommand> handler, CancellationToken ct) =>
        {
            Result result = await handler.HandleAsync(new MarkCrmMeetingNoShowCommand(meetingId), ct);
            return result.Match(Results.NoContent, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Meetings.MarkNoShow);

        meetings.MapGet("/{meetingId:guid}", async (
            Guid meetingId, IQueryHandler<GetCrmMeetingByIdQuery, CrmMeetingResponse> handler, CancellationToken ct) =>
        {
            Result<CrmMeetingResponse> result = await handler.HandleAsync(new GetCrmMeetingByIdQuery(meetingId), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Meetings.Read);

        meetings.MapGet("/", async (
            Guid projectId, int pageNumber, int pageSize,
            IQueryHandler<GetCrmMeetingsByProjectQuery, PagedResult<CrmMeetingResponse>> handler, CancellationToken ct) =>
        {
            Result<PagedResult<CrmMeetingResponse>> result = await handler.HandleAsync(new GetCrmMeetingsByProjectQuery(projectId, pageNumber, pageSize), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Meetings.Read);

        meetings.MapGet("/free-slots", async (
            Guid projectId, DateTime fromUtc, DateTime toUtc,
            IQueryHandler<GetFreeMeetingSlotsQuery, IReadOnlyList<FreeSlotResponse>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<FreeSlotResponse>> result = await handler.HandleAsync(new GetFreeMeetingSlotsQuery(projectId, fromUtc, toUtc), ct);
            return result.Match(Results.Ok, r => r.ToProblem());
        }).RequirePermission(CrmPermissions.Meetings.Read);
    }

    private sealed record ProposeMeetingRequest(
        Guid ProjectId, string Title, MeetingType Type, IReadOnlyList<CrmMeetingSlotRequest> Slots,
        Guid? PeriodId = null, string? Location = null, int? DurationMinutes = null);

    private sealed record DeclineSlotRequest(IReadOnlyList<CrmMeetingSlotRequest>? CounterSlots = null);
    private sealed record RescheduleMeetingRequest(DateTime StartsAtUtc, DateTime EndsAtUtc);
    private sealed record CancelMeetingRequest(string Reason);
}
