using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Meetings;
using FluentValidation;
using SharedKernel;

namespace Crm.Application.Features.Meetings;

internal sealed class GetFreeMeetingSlotsQueryValidator : AbstractValidator<GetFreeMeetingSlotsQuery>
{
    public GetFreeMeetingSlotsQueryValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x).Must(q => q.ToUtc > q.FromUtc).WithMessage("ToUtc must be after FromUtc.");
        RuleFor(x => x).Must(q => (q.ToUtc - q.FromUtc).TotalDays <= 60)
            .WithMessage("The free-slots window cannot exceed 60 days.");
    }
}

/// <summary><c>ProjectId</c> exists on this query purely so the endpoint can require
/// <c>crm.meetings.read</c> against a permission-gated route the same way every other read does — it is
/// never passed to <see cref="ICrmSchedulerService"/>, matching the backend's own behaviour: office
/// hours are the same for every project, not a per-project or per-employee calendar.</summary>
internal sealed class GetFreeMeetingSlotsQueryHandler(ICrmSchedulerService scheduler)
    : IQueryHandler<GetFreeMeetingSlotsQuery, IReadOnlyList<FreeSlotResponse>>
{
    public async Task<Result<IReadOnlyList<FreeSlotResponse>>> HandleAsync(
        GetFreeMeetingSlotsQuery query, CancellationToken ct = default)
    {
        var slots = await scheduler.GetFreeSlotsAsync(query.FromUtc, query.ToUtc, ct);
        List<FreeSlotResponse> items = slots
            .Select(s => new FreeSlotResponse(s.StartsAtUtc, s.EndsAtUtc))
            .ToList();

        return items;
    }
}
