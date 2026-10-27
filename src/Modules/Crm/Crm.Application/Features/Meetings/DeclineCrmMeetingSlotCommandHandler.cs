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
    }
}

internal sealed class DeclineCrmMeetingSlotByContactCommandValidator : AbstractValidator<DeclineCrmMeetingSlotByContactCommand>
{
    public DeclineCrmMeetingSlotByContactCommandValidator()
    {
        RuleFor(x => x.SlotId).NotEmpty();
        RuleFor(x => x.ActingContactId).NotEmpty();
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
            return CrmMeetingErrors.SlotNotFound;
        }

        if (!MeetingSupport.IsCounterpartyKind(slot, actingEmployeeId, actingContactId))
        {
            return CrmMeetingErrors.NotCounterparty;
        }

        Guid? meetingProjectId = await db.CrmMeetings.AsNoTracking()
            .Where(m => m.Id == slot.MeetingId)
            .Select(m => (Guid?)m.ProjectId)
            .FirstOrDefaultAsync(ct);
        if (meetingProjectId is null)
        {
            return CrmMeetingErrors.NotFound;
        }

        Result scoped = await MeetingSupport.EnsureActorCanActOnProjectAsync(
            db, projectDirectory, meetingProjectId.Value, actingEmployeeId, actingContactId, ct);
        if (scoped.IsFailure)
        {
            return scoped.Error;
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

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
