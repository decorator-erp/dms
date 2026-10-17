using BuildingBlocks.Messaging;
using Crm.Contracts.Meetings;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Meetings;

internal sealed class CompleteCrmMeetingCommandValidator : AbstractValidator<CompleteCrmMeetingCommand>
{
    public CompleteCrmMeetingCommandValidator() => RuleFor(x => x.MeetingId).NotEmpty();
}

/// <summary>Office-only — gated by <c>crm.meetings.complete</c>, held by office roles alone, so unlike
/// every other Meeting write this one carries no actor to check.</summary>
internal sealed class CompleteCrmMeetingCommandHandler(CrmDbContext db) : ICommandHandler<CompleteCrmMeetingCommand>
{
    public async Task<Result> HandleAsync(CompleteCrmMeetingCommand command, CancellationToken ct = default)
    {
        CrmMeeting? meeting = await db.CrmMeetings.FirstOrDefaultAsync(m => m.Id == command.MeetingId, ct);
        if (meeting is null)
        {
            return CrmMeetingErrors.NotFound;
        }

        Result completed = meeting.Complete(DateTime.UtcNow);
        if (completed.IsFailure)
        {
            return completed.Error;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
