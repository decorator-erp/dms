using BuildingBlocks.Messaging;
using Crm.Contracts.Meetings;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Meetings;

internal sealed class CancelCrmMeetingCommandValidator : AbstractValidator<CancelCrmMeetingCommand>
{
    public CancelCrmMeetingCommandValidator()
    {
        RuleFor(x => x.MeetingId).NotEmpty();
        RuleFor(x => x).Must(c => c.ActingEmployeeId.HasValue != c.ActingContactId.HasValue)
            .WithMessage("Exactly one of ActingEmployeeId or ActingContactId must be set.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

/// <summary>Matches <c>CrmMeetingDomainService.CancelAsync</c>: cancels the meeting, then declines
/// every slot still <see cref="MeetingSlotStatus.Proposed"/> — an accepted slot has already been
/// superseded by the meeting's own <c>Scheduled</c>→<c>Canceled</c> transition and is left as-is,
/// matching the backend.</summary>
internal sealed class CancelCrmMeetingCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<CancelCrmMeetingCommand>
{
    public async Task<Result> HandleAsync(CancelCrmMeetingCommand command, CancellationToken ct = default)
    {
        CrmMeeting? meeting = await db.CrmMeetings.FirstOrDefaultAsync(m => m.Id == command.MeetingId, ct);
        if (meeting is null)
        {
            return CrmMeetingErrors.NotFound;
        }

        Result scoped = await MeetingSupport.EnsureActorCanActOnProjectAsync(
            db, projectDirectory, meeting.ProjectId, command.ActingEmployeeId, command.ActingContactId, ct);
        if (scoped.IsFailure)
        {
            return scoped.Error;
        }

        DateTime now = DateTime.UtcNow;
        Result canceled = meeting.Cancel(command.Reason, now);
        if (canceled.IsFailure)
        {
            return canceled.Error;
        }

        List<CrmMeetingSlot> proposed = await db.CrmMeetingSlots
            .Where(s => s.MeetingId == command.MeetingId && s.Status == MeetingSlotStatus.Proposed)
            .ToListAsync(ct);
        foreach (CrmMeetingSlot? slot in proposed)
        {
            Result declined = slot.Decline();
            if (declined.IsFailure)
            {
                return declined.Error;
            }
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
