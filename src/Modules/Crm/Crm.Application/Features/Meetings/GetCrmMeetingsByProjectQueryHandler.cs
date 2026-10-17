using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Crm.Contracts.Meetings;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Meetings;

internal sealed class GetCrmMeetingsByProjectQueryValidator : AbstractValidator<GetCrmMeetingsByProjectQuery>
{
    public GetCrmMeetingsByProjectQueryValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

/// <summary>Matches the backend's <c>Search</c>: <c>IsActive</c>-filtered, newest first — unlike
/// <see cref="GetCrmMeetingByIdQueryHandler"/>, which has no such filter. A soft-deleted meeting is
/// fetchable by its own id but absent from this list, same as the backend.</summary>
internal sealed class GetCrmMeetingsByProjectQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmMeetingsByProjectQuery, PagedResult<CrmMeetingResponse>>
{
    public async Task<Result<PagedResult<CrmMeetingResponse>>> HandleAsync(
        GetCrmMeetingsByProjectQuery query, CancellationToken ct = default)
    {
        IQueryable<CrmMeeting> baseQuery = db.CrmMeetings.AsNoTracking()
            .Where(m => m.ProjectId == query.ProjectId && m.IsActive);

        int totalCount = await baseQuery.CountAsync(ct);

        var meetings = await baseQuery
            .OrderByDescending(m => m.CreatedAtUtc)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(m => new
            {
                m.Id,
                m.ProjectId,
                m.PeriodId,
                m.Title,
                m.Type,
                m.Status,
                m.ScheduledAtUtc,
                m.DurationMinutes,
                m.Location,
                m.RequestedByEmployeeId,
                m.RequestedByContactId,
                m.CanceledAtUtc,
                m.CancelReason,
                m.CreatedAtUtc,
            })
            .ToListAsync(ct);

        var meetingIds = meetings.Select(m => m.Id).ToList();
        var slotsByMeeting = (await db.CrmMeetingSlots.AsNoTracking()
                .Where(s => meetingIds.Contains(s.MeetingId))
                .ToListAsync(ct))
            .GroupBy(s => s.MeetingId)
            .ToDictionary(g => g.Key, g => g.Select(MeetingSupport.ToSlotResponse).ToList());

        var items = meetings
            .Select(m => new CrmMeetingResponse(
                m.Id, m.ProjectId, m.PeriodId, m.Title,
                CrmEnumMapping.ToContracts(m.Type), CrmEnumMapping.ToContracts(m.Status),
                m.ScheduledAtUtc, m.DurationMinutes, m.Location, m.RequestedByEmployeeId, m.RequestedByContactId,
                m.CanceledAtUtc, m.CancelReason, m.CreatedAtUtc,
                slotsByMeeting.TryGetValue(m.Id, out List<CrmMeetingSlotResponse>? slots) ? slots : []))
            .ToList();

        return new PagedResult<CrmMeetingResponse>(items, query.PageNumber, query.PageSize, totalCount);
    }
}
