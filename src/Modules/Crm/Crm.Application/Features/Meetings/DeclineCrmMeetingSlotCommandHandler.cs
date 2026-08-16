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
        RuleFor(x => x).Must(c => c.ActingEmployeeId.HasValue != c.ActingContactId.HasValue)
            .WithMessage("Exactly one of ActingEmployeeId or ActingContactId must be set.");
    }
}

/// <summary>Matches <c>CrmMeetingDomainService.DeclineSlotAsync</c>: decline the named slot, then
/// propose whichever counter-slots came with the call, stamped with the declining actor as their
/// proposer — same-transaction, no separate follow-up command needed.</summary>
internal sealed class DeclineCrmMeetingSlotCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<DeclineCrmMeetingSlotCommand>
{
    public async Task<Result> HandleAsync(DeclineCrmMeetingSlotCommand command, CancellationToken ct = default)
    {
        var slot = await db.CrmMeetingSlots.FirstOrDefaultAsync(s => s.Id == command.SlotId, ct);
        if (slot is null)
            return CrmMeetingErrors.SlotNotFound;

        if (!MeetingSupport.IsCounterpartyKind(slot, command.ActingEmployeeId, command.ActingContactId))
            return CrmMeetingErrors.NotCounterparty;

        var meetingProjectId = await db.CrmMeetings.AsNoTracking()
            .Where(m => m.Id == slot.MeetingId)
            .Select(m => (Guid?)m.ProjectId)
            .FirstOrDefaultAsync(ct);
        if (meetingProjectId is null)
            return CrmMeetingErrors.NotFound;

        var scoped = await MeetingSupport.EnsureActorCanActOnProjectAsync(
            db, projectDirectory, meetingProjectId.Value, command.ActingEmployeeId, command.ActingContactId, ct);
        if (scoped.IsFailure)
            return scoped.Error;

        var declined = slot.Decline();
        if (declined.IsFailure)
            return declined.Error;

        if (command.CounterSlots is { Count: > 0 })
        {
            var now = DateTime.UtcNow;
            foreach (var s in command.CounterSlots)
            {
                var counterResult = CrmMeetingSlot.Create(
                    slot.MeetingId, s.StartsAtUtc, s.EndsAtUtc,
                    proposedByEmployeeId: command.ActingEmployeeId, proposedByContactId: command.ActingContactId,
                    nowUtc: now);
                if (counterResult.IsFailure)
                    return counterResult.Error;

                db.CrmMeetingSlots.Add(counterResult.Value!);
            }
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
