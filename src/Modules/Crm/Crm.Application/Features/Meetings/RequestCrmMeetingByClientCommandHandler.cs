using BuildingBlocks.Messaging;
using Crm.Contracts.Meetings;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Projects.Contracts;
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
///
/// <para>X-2: the project must be the contact's own client's. Any failure of that check (another client's
/// project, an unknown project, a contact with no client) is the portal's 404, so a caller cannot probe which
/// projects exist. Only this create maps to 404; the slot and meeting operations keep their own errors.</para>
/// </summary>
internal sealed class RequestCrmMeetingByClientCommandHandler(CrmDbContext db, IProjectDirectory projectDirectory)
    : ICommandHandler<RequestCrmMeetingByClientCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(RequestCrmMeetingByClientCommand command, CancellationToken ct = default)
    {
        Result ownProject = await MeetingSupport.EnsureActorCanActOnProjectAsync(
            db, projectDirectory, command.ProjectId, null, command.RequestedByContactId, ct);
        // A-15: an inactive project is as missing to the client as another client's.
        if (ownProject.IsFailure || await projectDirectory.GetProjectAsync(command.ProjectId, ct) is not { IsActive: true })
        {
            return Result.Failure<Guid>(CrmPortalErrors.NotFound);
        }

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
        Result saved = await MeetingWrites.SaveAsync(db, ct);

        return saved.IsFailure ? Result.Failure<Guid>(saved.Error) : meeting.Id;
    }
}
