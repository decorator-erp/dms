using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Crm.Contracts.Meetings;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Meetings;

internal sealed class RescheduleCrmMeetingCommandValidator : AbstractValidator<RescheduleCrmMeetingCommand>
{
    public RescheduleCrmMeetingCommandValidator()
    {
        RuleFor(x => x.MeetingId).NotEmpty();
        RuleFor(x => x).Must(c => c.ActingEmployeeId.HasValue != c.ActingContactId.HasValue)
            .WithMessage("Exactly one of ActingEmployeeId or ActingContactId must be set.");
        RuleFor(x => x).Must(c => c.EndsAtUtc > c.StartsAtUtc).WithMessage("EndsAtUtc must be after StartsAtUtc.");
    }
}

/// <summary>Matches <c>CrmMeetingDomainService.RescheduleAsync</c>: moves the meeting to
/// <see cref="MeetingStatus.Rescheduled"/>, demotes whichever slot is currently
/// <see cref="MeetingSlotStatus.Accepted"/> (so the next accept doesn't collide with
/// <c>ux_crm_meeting_slots_meeting_accepted</c>), and proposes the new time. <c>Demote()</c> is
/// <c>internal</c> to <c>Crm.Domain</c> — visible here via that project's
/// <c>InternalsVisibleTo Crm.Application</c> grant, so it needs no public surface widening.</summary>
internal sealed class RescheduleCrmMeetingCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<RescheduleCrmMeetingCommand>
{
    public async Task<Result> HandleAsync(RescheduleCrmMeetingCommand command, CancellationToken ct = default)
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

        Result rescheduling = meeting.MarkRescheduling(DateTime.UtcNow);
        if (rescheduling.IsFailure)
        {
            return rescheduling.Error;
        }

        CrmMeetingSlot? accepted = await db.CrmMeetingSlots.FirstOrDefaultAsync(
            s => s.MeetingId == command.MeetingId && s.Status == MeetingSlotStatus.Accepted, ct);
        accepted?.Demote();

        Result<CrmMeetingSlot> newSlot = CrmMeetingSlot.Create(
            command.MeetingId, command.StartsAtUtc, command.EndsAtUtc,
            proposedByEmployeeId: command.ActingEmployeeId, proposedByContactId: command.ActingContactId,
            nowUtc: DateTime.UtcNow);
        if (newSlot.IsFailure)
        {
            return newSlot.Error;
        }

        db.CrmMeetingSlots.Add(newSlot.Value!);
        db.Outbox().Enqueue(new Crm.Contracts.MeetingRescheduledIntegrationEvent(
            Guid.NewGuid(), meeting.Id, meeting.ProjectId, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
