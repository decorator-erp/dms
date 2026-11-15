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
        RuleFor(x => x.FromUtc).MustBeUtc();
        RuleFor(x => x.ToUtc).MustBeUtc();
        RuleFor(x => x).Must(q => q.ToUtc > q.FromUtc).WithMessage("ToUtc must be after FromUtc.");
        RuleFor(x => x).Must(q => (q.ToUtc - q.FromUtc).TotalDays <= FreeMeetingSlots.MaxWindowDays)
            .WithMessage(FreeMeetingSlots.WindowTooLongMessage);
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
        // Guid.Empty matches no meeting, so a caller who is not an employee only contributes the project.
        Guid caller = await hrDirectory.ResolveEmployeeIdAsync(query.UserId, ct) ?? Guid.Empty;
        return Result.Success(await FreeMeetingSlots.ComputeAsync(scheduler, db, query.ProjectId, caller, query.FromUtc, query.ToUtc, ct));
    }
}

/// <summary>UI-1: the free-slot computation, shared by the office query above and the portal's
/// <c>GetMyPortalFreeMeetingSlotsQuery</c> — each handler authorizes its own caller, then both answer from here.</summary>
internal static class FreeMeetingSlots
{
    public const int MaxWindowDays = 60;
    public const string WindowTooLongMessage = "The free-slots window cannot exceed 60 days.";

    /// <param name="callerEmployeeId">The calling employee, whose own Scheduled meetings are busy too;
    /// <see cref="Guid.Empty"/> for a caller who is not one (matches no meeting).</param>
    public static async Task<IReadOnlyList<FreeSlotResponse>> ComputeAsync(
        ICrmSchedulerService scheduler, CrmDbContext db, Guid projectId, Guid callerEmployeeId,
        DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        DateTime from = fromUtc < DateTime.UtcNow ? DateTime.UtcNow : fromUtc;
        if (from >= toUtc)
        {
            return new List<FreeSlotResponse>();
        }

        IReadOnlyList<CrmFreeSlot> slots = await scheduler.GetFreeSlotsAsync(from, toUtc, ct);

        var booked = await db.CrmMeetings.AsNoTracking()
            .Where(m => m.IsActive && m.Status == Domain.MeetingStatus.Scheduled
                && m.ScheduledAtUtc != null && m.ScheduledAtUtc < toUtc
                && (m.ProjectId == projectId
                    || m.RequestedByEmployeeId == callerEmployeeId
                    || m.AcceptedByEmployeeId == callerEmployeeId))
            .Select(m => new { Start = m.ScheduledAtUtc!.Value, m.DurationMinutes })
            .ToListAsync(ct);

        var busy = booked.Select(b => (b.Start, End: b.Start.AddMinutes(b.DurationMinutes ?? 60))).ToList();
        return slots
            .Where(s => !busy.Any(b => s.StartsAtUtc < b.End && b.Start < s.EndsAtUtc))
            .Select(s => new FreeSlotResponse(s.StartsAtUtc, s.EndsAtUtc))
            .ToList();
    }
}
