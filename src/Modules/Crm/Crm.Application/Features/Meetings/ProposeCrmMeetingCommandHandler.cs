using BuildingBlocks.Messaging;
using Crm.Contracts.Meetings;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Crm.Application.Features.Meetings;

internal sealed class ProposeCrmMeetingCommandValidator : AbstractValidator<ProposeCrmMeetingCommand>
{
    public ProposeCrmMeetingCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.RequestedByEmployeeId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Location).MaximumLength(500);
        RuleFor(x => x.Slots).NotEmpty();
    }
}

/// <summary>The office-side create — the backend's <c>ProposeMeetingCommandHandler</c>, minus the
/// employee re-lookup: the endpoint already resolved <c>RequestedByEmployeeId</c> via
/// <c>CrmEndpointActors</c>, so there is nothing left to re-validate against HR here.</summary>
internal sealed class ProposeCrmMeetingCommandHandler(CrmDbContext db) : ICommandHandler<ProposeCrmMeetingCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(ProposeCrmMeetingCommand command, CancellationToken ct = default)
    {
        if (command.Slots.Count == 0)
            return Result.Failure<Guid>(CrmMeetingErrors.NoSlotsProvided);

        var periodCheck = await MeetingSupport.ValidatePeriodAsync(db, command.ProjectId, command.PeriodId, ct);
        if (periodCheck.IsFailure)
            return Result.Failure<Guid>(periodCheck.Error);

        var meetingResult = CrmMeeting.Create(
            command.ProjectId, command.PeriodId, command.Title, CrmEnumMapping.ToDomain(command.Type),
            requestedByEmployeeId: command.RequestedByEmployeeId, requestedByContactId: null,
            location: command.Location, durationMinutes: command.DurationMinutes);
        if (meetingResult.IsFailure)
            return Result.Failure<Guid>(meetingResult.Error);

        var meeting = meetingResult.Value!;
        var now = DateTime.UtcNow;
        var slots = new List<CrmMeetingSlot>();
        foreach (var s in command.Slots)
        {
            var slotResult = CrmMeetingSlot.Create(
                meeting.Id, s.StartsAtUtc, s.EndsAtUtc,
                proposedByEmployeeId: command.RequestedByEmployeeId, proposedByContactId: null, nowUtc: now);
            if (slotResult.IsFailure)
                return Result.Failure<Guid>(slotResult.Error);

            slots.Add(slotResult.Value!);
        }

        db.CrmMeetings.Add(meeting);
        db.CrmMeetingSlots.AddRange(slots);
        await db.SaveChangesAsync(ct);

        return meeting.Id;
    }
}
