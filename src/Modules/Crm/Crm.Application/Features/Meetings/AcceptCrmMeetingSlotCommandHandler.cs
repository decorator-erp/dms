using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Crm.Contracts;
using Crm.Contracts.Meetings;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Meetings;

internal sealed class AcceptCrmMeetingSlotCommandValidator : AbstractValidator<AcceptCrmMeetingSlotCommand>
{
    public AcceptCrmMeetingSlotCommandValidator()
    {
        RuleFor(x => x.SlotId).NotEmpty();
        RuleFor(x => x).Must(c => c.ActingEmployeeId.HasValue != c.ActingContactId.HasValue)
            .WithMessage("Exactly one of ActingEmployeeId or ActingContactId must be set.");
    }
}

/// <summary>Accepting declines every sibling proposed slot and schedules the meeting, matching
/// <c>CrmMeetingDomainService.AcceptSlotAsync</c>. <c>MeetingScheduledIntegrationEvent</c> is enqueued
/// before <c>SaveChangesAsync</c> — same transaction, per the contract's own precedent
/// (<c>ConvertCrmLeadCommandHandler</c>'s <c>LeadConvertedIntegrationEvent</c>). See
/// <c>MeetingSupport.EnsureActorCanActOnProjectAsync</c> for the tenant-boundary fix this handler
/// applies over the ported backend's actor check.</summary>
internal sealed class AcceptCrmMeetingSlotCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<AcceptCrmMeetingSlotCommand>
{
    public async Task<Result> HandleAsync(AcceptCrmMeetingSlotCommand command, CancellationToken ct = default)
    {
        CrmMeetingSlot? slot = await db.CrmMeetingSlots.FirstOrDefaultAsync(s => s.Id == command.SlotId, ct);
        if (slot is null)
        {
            return CrmMeetingErrors.SlotNotFound;
        }

        if (!MeetingSupport.IsCounterpartyKind(slot, command.ActingEmployeeId, command.ActingContactId))
        {
            return CrmMeetingErrors.NotCounterparty;
        }

        CrmMeeting? meeting = await db.CrmMeetings.FirstOrDefaultAsync(m => m.Id == slot.MeetingId, ct);
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

        Result accepted = slot.Accept();
        if (accepted.IsFailure)
        {
            return accepted.Error;
        }

        // One accepted slot per meeting - decline every other still-proposed slot in the same SaveChanges.
        List<CrmMeetingSlot> siblings = await db.CrmMeetingSlots
            .Where(s => s.MeetingId == slot.MeetingId && s.Id != slot.Id && s.Status == Domain.MeetingSlotStatus.Proposed)
            .ToListAsync(ct);
        foreach (CrmMeetingSlot? sibling in siblings)
        {
            Result declined = sibling.Decline();
            if (declined.IsFailure)
            {
                return declined.Error;
            }
        }

        int duration = meeting.DurationMinutes ?? (int)Math.Ceiling((slot.EndsAtUtc - slot.StartsAtUtc).TotalMinutes);
        Result scheduled = meeting.Schedule(slot.StartsAtUtc, duration);
        if (scheduled.IsFailure)
        {
            return scheduled.Error;
        }

        db.Outbox().Enqueue(new MeetingScheduledIntegrationEvent(
            Guid.NewGuid(), meeting.Id, meeting.ProjectId, slot.StartsAtUtc, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
