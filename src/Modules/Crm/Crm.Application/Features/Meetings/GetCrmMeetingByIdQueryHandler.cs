using BuildingBlocks.Messaging;
using Crm.Contracts.Meetings;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using SharedKernel;

namespace Crm.Application.Features.Meetings;

/// <summary>No <c>IsActive</c> filter — deliberately matching the backend's own <c>GetDetails</c>, which
/// has none either (unlike <c>Search</c>, below). Fetching a soft-deleted meeting by its own id is not
/// the same operation as listing active ones.</summary>
internal sealed class GetCrmMeetingByIdQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmMeetingByIdQuery, CrmMeetingResponse>
{
    public async Task<Result<CrmMeetingResponse>> HandleAsync(GetCrmMeetingByIdQuery query, CancellationToken ct = default)
    {
        var response = await MeetingSupport.ToResponseAsync(db, query.MeetingId, ct);
        return response is null
            ? Result.Failure<CrmMeetingResponse>(CrmMeetingErrors.NotFound)
            : response;
    }
}
