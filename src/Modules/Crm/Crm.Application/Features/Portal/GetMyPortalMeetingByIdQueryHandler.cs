using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Portal;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Portal;

/// <summary>
/// B-1 (H2/X-7, MT-N1): the client's view of one of its meetings, with the slots it has to answer. The
/// meeting must be active and on a project of the caller's own client; anything else — another client's
/// meeting, an unknown id — is the same 404, so a caller cannot probe meeting ids. Office meeting reads stay
/// behind <c>crm.meetings.read</c>, which the client never holds.
/// </summary>
internal sealed class GetMyPortalMeetingByIdQueryHandler(CrmDbContext db, ICrmAccessGuard guard, IProjectDirectory projectDirectory)
    : IQueryHandler<GetMyPortalMeetingByIdQuery, PortalMeetingDetailResponse>
{
    public async Task<Result<PortalMeetingDetailResponse>> HandleAsync(GetMyPortalMeetingByIdQuery query, CancellationToken ct = default)
    {
        Result<Guid> clientId = await PortalClients.ResolveClientIdAsync(guard, query.AppUserId, ct);
        if (clientId.IsFailure)
        {
            return Result.Failure<PortalMeetingDetailResponse>(clientId.Error);
        }

        CrmMeeting? meeting = await db.CrmMeetings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == query.MeetingId && m.IsActive, ct);
        if (meeting is null || (await projectDirectory.GetProjectAsync(meeting.ProjectId, ct))?.ClientId != clientId.Value)
        {
            return Result.Failure<PortalMeetingDetailResponse>(CrmPortalErrors.NotFound);
        }

        List<CrmMeetingSlot> slots = await db.CrmMeetingSlots.AsNoTracking()
            .Where(s => s.MeetingId == meeting.Id)
            .OrderBy(s => s.StartsAtUtc)
            .ToListAsync(ct);

        return new PortalMeetingDetailResponse(
            meeting.Id, meeting.ProjectId, meeting.Title, CrmEnumMapping.ToContracts(meeting.Type),
            CrmEnumMapping.ToContracts(meeting.Status), meeting.ScheduledAtUtc, meeting.DurationMinutes, meeting.Location,
            meeting.CanceledAtUtc, meeting.CancelReason,
            [.. slots.Select(s => new PortalMeetingSlotResponse(
                s.Id, s.StartsAtUtc, s.EndsAtUtc, CrmEnumMapping.ToContracts(s.Status), ProposedByMe: s.ProposedByContactId is not null))]);
    }
}
