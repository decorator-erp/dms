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
        RuleFor(x => x.ActingEmployeeId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x).Must(c => c.EndsAtUtc > c.StartsAtUtc).WithMessage("EndsAtUtc must be after StartsAtUtc.");
    }
}

internal sealed class RescheduleCrmMeetingByContactCommandValidator : AbstractValidator<RescheduleCrmMeetingByContactCommand>
{
    public RescheduleCrmMeetingByContactCommandValidator()
    {
        RuleFor(x => x.MeetingId).NotEmpty();
        RuleFor(x => x.ActingContactId).NotEmpty();
        RuleFor(x => x).Must(c => c.EndsAtUtc > c.StartsAtUtc).WithMessage("EndsAtUtc must be after StartsAtUtc.");
    }
}

/// <summary>B-3 (M1): the office reschedule, project-scoped by the decorator (target Meeting).</summary>
internal sealed class RescheduleCrmMeetingCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<RescheduleCrmMeetingCommand>
{
    public Task<Result> HandleAsync(RescheduleCrmMeetingCommand command, CancellationToken ct = default) =>
        RescheduleMeeting.RunAsync(db, projectDirectory, command.MeetingId, command.ActingEmployeeId, null,
            command.StartsAtUtc, command.EndsAtUtc, ct);
}

/// <summary>B-3 (M1): the portal reschedule — not project-scoped; the core checks the contact's client.</summary>
internal sealed class RescheduleCrmMeetingByContactCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<RescheduleCrmMeetingByContactCommand>
{
    public Task<Result> HandleAsync(RescheduleCrmMeetingByContactCommand command, CancellationToken ct = default) =>
        RescheduleMeeting.RunAsync(db, projectDirectory, command.MeetingId, null, command.ActingContactId,
            command.StartsAtUtc, command.EndsAtUtc, ct);
}

internal static class RescheduleMeeting
{
    public static async Task<Result> RunAsync(
        CrmDbContext db, IProjectDirectory projectDirectory, Guid meetingId, Guid? actingEmployeeId, Guid? actingContactId,
        DateTime startsAtUtc, DateTime endsAtUtc, CancellationToken ct)
    {
        CrmMeeting? meeting = await db.CrmMeetings.FirstOrDefaultAsync(m => m.Id == meetingId, ct);
        if (meeting is null)
        {
            return MeetingSupport.NotFoundFor(actingContactId, CrmMeetingErrors.NotFound);
        }

        // B-4 (MT-N2): a stranger contact gets the portal 404. Employees are scoped by the decorator (B-3).
        Result scoped = await MeetingSupport.EnsureContactOwnsProjectAsync(
            db, projectDirectory, meeting.ProjectId, actingContactId, ct);
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
            s => s.MeetingId == meetingId && s.Status == MeetingSlotStatus.Accepted, ct);
        accepted?.Demote();

        Result<CrmMeetingSlot> newSlot = CrmMeetingSlot.Create(
            meetingId, startsAtUtc, endsAtUtc,
            proposedByEmployeeId: actingEmployeeId, proposedByContactId: actingContactId,
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
