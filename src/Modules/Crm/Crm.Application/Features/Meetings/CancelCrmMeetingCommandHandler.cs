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

internal sealed class CancelCrmMeetingCommandValidator : AbstractValidator<CancelCrmMeetingCommand>
{
    public CancelCrmMeetingCommandValidator()
    {
        RuleFor(x => x.MeetingId).NotEmpty();
        RuleFor(x => x.ActingEmployeeId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

internal sealed class CancelCrmMeetingByContactCommandValidator : AbstractValidator<CancelCrmMeetingByContactCommand>
{
    public CancelCrmMeetingByContactCommandValidator()
    {
        RuleFor(x => x.MeetingId).NotEmpty();
        RuleFor(x => x.ActingContactId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

/// <summary>B-3 (M1): the office cancel, project-scoped by the decorator (target Meeting).</summary>
internal sealed class CancelCrmMeetingCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<CancelCrmMeetingCommand>
{
    public Task<Result> HandleAsync(CancelCrmMeetingCommand command, CancellationToken ct = default) =>
        CancelMeeting.RunAsync(db, projectDirectory, command.MeetingId, null, command.Reason, ct);
}

/// <summary>B-3 (M1): the portal cancel — not project-scoped; the core checks the contact's client.</summary>
internal sealed class CancelCrmMeetingByContactCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<CancelCrmMeetingByContactCommand>
{
    public Task<Result> HandleAsync(CancelCrmMeetingByContactCommand command, CancellationToken ct = default) =>
        CancelMeeting.RunAsync(db, projectDirectory, command.MeetingId, command.ActingContactId, command.Reason, ct);
}

internal static class CancelMeeting
{
    public static async Task<Result> RunAsync(
        CrmDbContext db, IProjectDirectory projectDirectory, Guid meetingId, Guid? actingContactId,
        string reason, CancellationToken ct)
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

        DateTime now = DateTime.UtcNow;
        Result canceled = meeting.Cancel(reason, now);
        if (canceled.IsFailure)
        {
            return canceled.Error;
        }

        List<CrmMeetingSlot> proposed = await db.CrmMeetingSlots
            .Where(s => s.MeetingId == meetingId && s.Status == MeetingSlotStatus.Proposed)
            .ToListAsync(ct);
        foreach (CrmMeetingSlot? slot in proposed)
        {
            Result declined = slot.Decline();
            if (declined.IsFailure)
            {
                return declined.Error;
            }
        }

        db.Outbox().Enqueue(new Crm.Contracts.MeetingCanceledIntegrationEvent(
            Guid.NewGuid(), meeting.Id, meeting.ProjectId, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
