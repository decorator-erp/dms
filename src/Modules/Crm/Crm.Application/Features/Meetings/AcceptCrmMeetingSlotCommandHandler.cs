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
        RuleFor(x => x.ActingEmployeeId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}

internal sealed class AcceptCrmMeetingSlotByContactCommandValidator : AbstractValidator<AcceptCrmMeetingSlotByContactCommand>
{
    public AcceptCrmMeetingSlotByContactCommandValidator()
    {
        RuleFor(x => x.SlotId).NotEmpty();
        RuleFor(x => x.ActingContactId).NotEmpty();
    }
}

/// <summary>B-3 (M1): the office accept. Project scope is the decorator's (<c>ICrmProjectScoped</c>, target
/// MeetingSlot), so an employee off the project gets 404 before this runs.</summary>
internal sealed class AcceptCrmMeetingSlotCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<AcceptCrmMeetingSlotCommand>
{
    public Task<Result> HandleAsync(AcceptCrmMeetingSlotCommand command, CancellationToken ct = default) =>
        AcceptMeetingSlot.RunAsync(db, projectDirectory, command.SlotId, command.ActingEmployeeId, null, ct);
}

/// <summary>B-3 (M1): the portal accept — not project-scoped; the shared core checks the contact's client
/// owns the meeting's project.</summary>
internal sealed class AcceptCrmMeetingSlotByContactCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<AcceptCrmMeetingSlotByContactCommand>
{
    public Task<Result> HandleAsync(AcceptCrmMeetingSlotByContactCommand command, CancellationToken ct = default) =>
        AcceptMeetingSlot.RunAsync(db, projectDirectory, command.SlotId, null, command.ActingContactId, ct);
}

/// <summary>Accepting declines every sibling proposed slot and schedules the meeting, matching
/// <c>CrmMeetingDomainService.AcceptSlotAsync</c>. <c>MeetingScheduledIntegrationEvent</c> is enqueued
/// before <c>SaveChangesAsync</c> — same transaction, per the contract's own precedent
/// (<c>ConvertCrmLeadCommandHandler</c>'s <c>LeadConvertedIntegrationEvent</c>). See
/// <c>MeetingSupport.EnsureActorCanActOnProjectAsync</c> for the tenant-boundary fix this applies over the
/// ported backend's actor check.</summary>
internal static class AcceptMeetingSlot
{
    public static async Task<Result> RunAsync(
        CrmDbContext db, IProjectDirectory projectDirectory, Guid slotId, Guid? actingEmployeeId, Guid? actingContactId,
        CancellationToken ct)
    {
        CrmMeetingSlot? slot = await db.CrmMeetingSlots.FirstOrDefaultAsync(s => s.Id == slotId, ct);
        if (slot is null)
        {
            return MeetingSupport.NotFoundFor(actingContactId, CrmMeetingErrors.SlotNotFound);
        }

        CrmMeeting? meeting = await db.CrmMeetings.FirstOrDefaultAsync(m => m.Id == slot.MeetingId, ct);
        if (meeting is null)
        {
            return MeetingSupport.NotFoundFor(actingContactId, CrmMeetingErrors.NotFound);
        }

        // B-4 (MT-N2): tenant before kind — a stranger learns nothing about the slot.
        Result scoped = await MeetingSupport.EnsureContactOwnsProjectAsync(db, projectDirectory, meeting.ProjectId, actingContactId, ct);
        if (scoped.IsFailure)
        {
            return scoped.Error;
        }

        if (!MeetingSupport.IsCounterpartyKind(slot, actingEmployeeId, actingContactId))
        {
            return CrmMeetingErrors.NotCounterparty;
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
        Result scheduled = meeting.Schedule(slot.StartsAtUtc, duration, actingEmployeeId);
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
