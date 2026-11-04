using BuildingBlocks.Messaging;
using Crm.Contracts.Meetings;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Meetings;

internal sealed class DeclineCrmMeetingSlotCommandValidator : AbstractValidator<DeclineCrmMeetingSlotCommand>
{
    public DeclineCrmMeetingSlotCommandValidator()
    {
        RuleFor(x => x.SlotId).NotEmpty();
        RuleFor(x => x.ActingEmployeeId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.CounterSlots).AreValidSlots();
    }
}

internal sealed class DeclineCrmMeetingSlotByContactCommandValidator : AbstractValidator<DeclineCrmMeetingSlotByContactCommand>
{
    public DeclineCrmMeetingSlotByContactCommandValidator()
    {
        RuleFor(x => x.SlotId).NotEmpty();
        RuleFor(x => x.ActingContactId).NotEmpty();
        RuleFor(x => x.CounterSlots).AreValidSlots();
    }
}

/// <summary>B-3 (M1): the office decline, project-scoped by the decorator (target MeetingSlot).</summary>
internal sealed class DeclineCrmMeetingSlotCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<DeclineCrmMeetingSlotCommand>
{
    public Task<Result> HandleAsync(DeclineCrmMeetingSlotCommand command, CancellationToken ct = default) =>
        DeclineMeetingSlot.RunAsync(db, projectDirectory, command.SlotId, command.ActingEmployeeId, null, command.CounterSlots, ct);
}

/// <summary>B-3 (M1): the portal decline — not project-scoped; the core checks the contact's client.</summary>
internal sealed class DeclineCrmMeetingSlotByContactCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<DeclineCrmMeetingSlotByContactCommand>
{
    public Task<Result> HandleAsync(DeclineCrmMeetingSlotByContactCommand command, CancellationToken ct = default) =>
        DeclineMeetingSlot.RunAsync(db, projectDirectory, command.SlotId, null, command.ActingContactId, command.CounterSlots, ct);
}

/// <summary>Matches <c>CrmMeetingDomainService.DeclineSlotAsync</c>: decline the named slot, then propose
/// whichever counter-slots came with the call, stamped with the declining actor as their proposer —
/// same-transaction, no separate follow-up command needed.</summary>
internal static class DeclineMeetingSlot
{
    public static async Task<Result> RunAsync(
        CrmDbContext db, IProjectDirectory projectDirectory, Guid slotId, Guid? actingEmployeeId, Guid? actingContactId,
        IReadOnlyList<CrmMeetingSlotRequest>? counterSlots, CancellationToken ct)
    {
        CrmMeetingSlot? slot = await db.CrmMeetingSlots.FirstOrDefaultAsync(s => s.Id == slotId, ct);
        if (slot is null)
        {
            return MeetingSupport.NotFoundFor(actingContactId, CrmMeetingErrors.SlotNotFound);
        }

        // D-13 (B-F2): the meeting is tracked, not read-only — the save below marks it modified, so a
        // decline and an accept of the same meeting collide on its xmin (one of them is a 409).
        CrmMeeting? meeting = await db.CrmMeetings.FirstOrDefaultAsync(m => m.Id == slot.MeetingId && m.IsActive, ct);
        if (meeting is null)
        {
            return MeetingSupport.NotFoundFor(actingContactId, CrmMeetingErrors.NotFound);
        }

        // FINDING-P1-16: same as the accept — read the slot again now that the meeting's xmin is held.
        if (!await MeetingSupport.RereadSlotAfterMeetingAsync(db, slot, ct))
        {
            return MeetingSupport.NotFoundFor(actingContactId, CrmMeetingErrors.SlotNotFound);
        }

        // B-4 (MT-N2): tenant before kind.
        Result scoped = await MeetingSupport.EnsureContactOwnsProjectAsync(
            db, projectDirectory, meeting.ProjectId, actingContactId, ct);
        if (scoped.IsFailure)
        {
            return scoped.Error;
        }

        if (!MeetingSupport.IsCounterpartyKind(slot, actingEmployeeId, actingContactId))
        {
            return CrmMeetingErrors.NotCounterparty;
        }

        // D-13 (B-F2): only a meeting still being arranged has slots to decline.
        if (meeting.Status is not (MeetingStatus.Requested or MeetingStatus.Rescheduled))
        {
            return CrmMeetingErrors.InvalidStateTransition;
        }

        Result declined = slot.Decline();
        if (declined.IsFailure)
        {
            return declined.Error;
        }

        if (counterSlots is { Count: > 0 })
        {
            DateTime now = DateTime.UtcNow;
            foreach (CrmMeetingSlotRequest s in counterSlots)
            {
                Result<CrmMeetingSlot> counterResult = CrmMeetingSlot.Create(
                    slot.MeetingId, s.StartsAtUtc, s.EndsAtUtc,
                    proposedByEmployeeId: actingEmployeeId, proposedByContactId: actingContactId,
                    nowUtc: now);
                if (counterResult.IsFailure)
                {
                    return counterResult.Error;
                }

                db.CrmMeetingSlots.Add(counterResult.Value!);
            }
        }

        db.Entry(meeting).State = EntityState.Modified;
        return await MeetingWrites.SaveAsync(db, ct);
    }
}
