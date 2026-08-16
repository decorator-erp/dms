using BuildingBlocks.Messaging;
using Crm.Contracts.Meetings;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Meetings;

internal sealed class MarkCrmMeetingNoShowCommandValidator : AbstractValidator<MarkCrmMeetingNoShowCommand>
{
    public MarkCrmMeetingNoShowCommandValidator()
    {
        RuleFor(x => x.MeetingId).NotEmpty();
    }
}

/// <summary>Office-only — gated by <c>crm.meetings.mark_no_show</c>. Byte-for-byte the same shape as
/// <see cref="CompleteCrmMeetingCommandHandler"/>, matching the backend's own two handlers.</summary>
internal sealed class MarkCrmMeetingNoShowCommandHandler(CrmDbContext db) : ICommandHandler<MarkCrmMeetingNoShowCommand>
{
    public async Task<Result> HandleAsync(MarkCrmMeetingNoShowCommand command, CancellationToken ct = default)
    {
        var meeting = await db.CrmMeetings.FirstOrDefaultAsync(m => m.Id == command.MeetingId, ct);
        if (meeting is null)
            return CrmMeetingErrors.NotFound;

        var noShow = meeting.MarkNoShow(DateTime.UtcNow);
        if (noShow.IsFailure)
            return noShow.Error;

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
