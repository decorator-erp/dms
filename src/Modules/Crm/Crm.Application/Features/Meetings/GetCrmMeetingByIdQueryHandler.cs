using BuildingBlocks.Messaging;
using Crm.Contracts.Meetings;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using SharedKernel;

namespace Crm.Application.Features.Meetings;

/// <summary>B-12 (L4): <c>IsActive</c>-filtered like the list — a soft-deleted meeting is 404.</summary>
internal sealed class GetCrmMeetingByIdQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmMeetingByIdQuery, CrmMeetingResponse>
{
    public async Task<Result<CrmMeetingResponse>> HandleAsync(GetCrmMeetingByIdQuery query, CancellationToken ct = default)
    {
        CrmMeetingResponse? response = await MeetingSupport.ToResponseAsync(db, query.MeetingId, ct);
        return response is null
            ? Result.Failure<CrmMeetingResponse>(CrmMeetingErrors.NotFound)
            : response;
    }
}
