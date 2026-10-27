using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Meetings;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Meetings;

internal sealed class GetFreeMeetingSlotsQueryValidator : AbstractValidator<GetFreeMeetingSlotsQuery>
{
    public GetFreeMeetingSlotsQueryValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x).Must(q => q.ToUtc > q.FromUtc).WithMessage("ToUtc must be after FromUtc.");
        RuleFor(x => x).Must(q => (q.ToUtc - q.FromUtc).TotalDays <= 60)
            .WithMessage("The free-slots window cannot exceed 60 days.");
    }
}

/// <summary><c>ProjectId</c> exists on this query purely so the endpoint can require
/// <c>crm.meetings.read</c> against a permission-gated route the same way every other read does — it is
/// never passed to <see cref="ICrmSchedulerService"/>, matching the backend's own behaviour: office
/// hours are the same for every project, not a per-project or per-employee calendar.</summary>
/// <summary>B-9 (M6, MT-N9): the office calendar's free slots (<see cref="ICrmSchedulerService"/>, which stays
/// pure) minus every Scheduled meeting that overlaps them — on this project, or involving the caller as
/// requester or acceptor. The window starts no earlier than now: a past slot would only fail at create.</summary>
internal sealed class GetFreeMeetingSlotsQueryHandler(ICrmSchedulerService scheduler, CrmDbContext db, IHrDirectory hrDirectory)
    : IQueryHandler<GetFreeMeetingSlotsQuery, IReadOnlyList<FreeSlotResponse>>
{
    public async Task<Result<IReadOnlyList<FreeSlotResponse>>> HandleAsync(
        GetFreeMeetingSlotsQuery query, CancellationToken ct = default)
    {
        DateTime fromUtc = query.FromUtc < DateTime.UtcNow ? DateTime.UtcNow : query.FromUtc;
        if (fromUtc >= query.ToUtc)
        {
            return new List<FreeSlotResponse>();
        }

        IReadOnlyList<CrmFreeSlot> slots = await scheduler.GetFreeSlotsAsync(fromUtc, query.ToUtc, ct);

        // Guid.Empty matches no meeting, so a caller who is not an employee only contributes the project.
        Guid caller = await hrDirectory.ResolveEmployeeIdAsync(query.UserId, ct) ?? Guid.Empty;
        var booked = await db.CrmMeetings.AsNoTracking()
            .Where(m => m.IsActive && m.Status == Domain.MeetingStatus.Scheduled
                && m.ScheduledAtUtc != null && m.ScheduledAtUtc < query.ToUtc
                && (m.ProjectId == query.ProjectId
                    || m.RequestedByEmployeeId == caller
                    || m.AcceptedByEmployeeId == caller))
            .Select(m => new { Start = m.ScheduledAtUtc!.Value, m.DurationMinutes })
            .ToListAsync(ct);

        var busy = booked.Select(b => (b.Start, End: b.Start.AddMinutes(b.DurationMinutes ?? 60))).ToList();
        var items = slots
            .Where(s => !busy.Any(b => s.StartsAtUtc < b.End && b.Start < s.EndsAtUtc))
            .Select(s => new FreeSlotResponse(s.StartsAtUtc, s.EndsAtUtc))
            .ToList();

        return items;
    }
}
