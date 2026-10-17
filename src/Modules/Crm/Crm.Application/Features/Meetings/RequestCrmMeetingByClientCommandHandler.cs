using BuildingBlocks.Messaging;
using Crm.Contracts.Meetings;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using SharedKernel;

namespace Crm.Application.Features.Meetings;

internal sealed class RequestCrmMeetingByClientCommandValidator : AbstractValidator<RequestCrmMeetingByClientCommand>
{
    public RequestCrmMeetingByClientCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.RequestedByContactId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Location).MaximumLength(500);
        RuleFor(x => x).Must(c => c.EndsAtUtc > c.StartsAtUtc).WithMessage("EndsAtUtc must be after StartsAtUtc.");
    }
}

/// <summary>The Portal-side create — the backend's <c>RequestMeetingByClientCommandHandler</c>. The
/// endpoint resolves <c>RequestedByContactId</c> via <c>CrmEndpointActors.ResolveContactIdAsync</c>,
/// which already fails the request when the caller has no contact on file (the backend's
/// <c>CrmContactErrors.NoContactOnFile</c> case), so the handler has nothing left to check there.
/// </summary>
internal sealed class RequestCrmMeetingByClientCommandHandler(CrmDbContext db)
    : ICommandHandler<RequestCrmMeetingByClientCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(RequestCrmMeetingByClientCommand command, CancellationToken ct = default)
    {
        Result periodCheck = await MeetingSupport.ValidatePeriodAsync(db, command.ProjectId, command.PeriodId, ct);
        if (periodCheck.IsFailure)
        {
            return Result.Failure<Guid>(periodCheck.Error);
        }

        Result<CrmMeeting> meetingResult = CrmMeeting.Create(
            command.ProjectId, command.PeriodId, command.Title, CrmEnumMapping.ToDomain(command.Type),
            requestedByEmployeeId: null, requestedByContactId: command.RequestedByContactId,
            location: command.Location);
        if (meetingResult.IsFailure)
        {
            return Result.Failure<Guid>(meetingResult.Error);
        }

        CrmMeeting meeting = meetingResult.Value!;
        Result<CrmMeetingSlot> slotResult = CrmMeetingSlot.Create(
            meeting.Id, command.StartsAtUtc, command.EndsAtUtc,
            proposedByEmployeeId: null, proposedByContactId: command.RequestedByContactId, nowUtc: DateTime.UtcNow);
        if (slotResult.IsFailure)
        {
            return Result.Failure<Guid>(slotResult.Error);
        }

        db.CrmMeetings.Add(meeting);
        db.CrmMeetingSlots.Add(slotResult.Value!);
        await db.SaveChangesAsync(ct);

        return meeting.Id;
    }
}
