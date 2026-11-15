using BuildingBlocks.Messaging;
using Crm.Application.Features.Meetings;
using Crm.Contracts;
using Crm.Contracts.Meetings;
using Crm.Contracts.Portal;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Portal;

internal sealed class GetMyPortalFreeMeetingSlotsQueryValidator : AbstractValidator<GetMyPortalFreeMeetingSlotsQuery>
{
    public GetMyPortalFreeMeetingSlotsQueryValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.FromUtc).MustBeUtc();
        RuleFor(x => x.ToUtc).MustBeUtc();
        RuleFor(x => x).Must(q => q.ToUtc > q.FromUtc).WithMessage("ToUtc must be after FromUtc.");
        RuleFor(x => x).Must(q => (q.ToUtc - q.FromUtc).TotalDays <= FreeMeetingSlots.MaxWindowDays)
            .WithMessage(FreeMeetingSlots.WindowTooLongMessage);
    }
}

/// <summary>UI-1: the portal's free slots — the office's <c>GetFreeMeetingSlotsQueryHandler</c> answer
/// (<see cref="FreeMeetingSlots"/>), for a client who holds <c>crm.meetings.request_own</c> but never
/// <c>crm.meetings.read</c>. The project check is the meeting request's own (X-2, A-15): the project must be the
/// caller's own client's and active; anything else is <see cref="CrmPortalErrors.NotFound"/>, so a client cannot
/// probe which projects exist. A client is no employee, so only the project's own Scheduled meetings are busy.</summary>
internal sealed class GetMyPortalFreeMeetingSlotsQueryHandler(
    CrmDbContext db, ICrmAccessGuard guard, IProjectDirectory projectDirectory, ICrmSchedulerService scheduler)
    : IQueryHandler<GetMyPortalFreeMeetingSlotsQuery, IReadOnlyList<FreeSlotResponse>>
{
    public async Task<Result<IReadOnlyList<FreeSlotResponse>>> HandleAsync(
        GetMyPortalFreeMeetingSlotsQuery query, CancellationToken ct = default)
    {
        Result<Guid> clientId = await PortalClients.ResolveClientIdAsync(guard, query.AppUserId, ct);
        if (clientId.IsFailure)
        {
            return Result.Failure<IReadOnlyList<FreeSlotResponse>>(clientId.Error);
        }

        ProjectSummary? project = await projectDirectory.GetProjectAsync(query.ProjectId, ct);
        if (project is not { IsActive: true } || project.ClientId != clientId.Value)
        {
            return Result.Failure<IReadOnlyList<FreeSlotResponse>>(CrmPortalErrors.NotFound);
        }

        return Result.Success(await FreeMeetingSlots.ComputeAsync(
            scheduler, db, query.ProjectId, Guid.Empty, query.FromUtc, query.ToUtc, ct));
    }
}
