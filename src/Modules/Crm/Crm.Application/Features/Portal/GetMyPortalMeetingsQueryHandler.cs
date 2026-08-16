using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Portal;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Portal;

/// <summary>
/// M1-02: closes the gap <c>GetMyPortalBriefQueryHandler</c>'s own doc comment named at G9-14 — this
/// query's own doc comment says it resolves "the calling client's own projects," and until now
/// <see cref="IProjectDirectory"/> had no client-scoped project lookup to answer that with. M1-02 adds
/// <see cref="IProjectDirectory.GetProjectsByClientAsync"/>; this handler is the first caller.
/// </summary>
internal sealed class GetMyPortalMeetingsQueryHandler(CrmDbContext db, ICrmAccessGuard guard, IProjectDirectory projectDirectory)
    : IQueryHandler<GetMyPortalMeetingsQuery, IReadOnlyList<PortalMeetingResponse>>
{
    public async Task<Result<IReadOnlyList<PortalMeetingResponse>>> HandleAsync(
        GetMyPortalMeetingsQuery query, CancellationToken ct = default)
    {
        var actor = await guard.ResolveActorAsync(query.AppUserId, ct);
        if (actor.IsFailure)
            return Result.Failure<IReadOnlyList<PortalMeetingResponse>>(actor.Error);
        if (actor.Value!.Kind != CrmActorKind.Client)
            return Result.Failure<IReadOnlyList<PortalMeetingResponse>>(CrmPortalErrors.NotAClient);

        var projects = await projectDirectory.GetProjectsByClientAsync(actor.Value.ClientId!.Value, ct);
        if (projects.Count == 0)
            return new List<PortalMeetingResponse>();

        var projectIds = projects.Select(p => p.Id).ToList();
        var meetings = await db.CrmMeetings.AsNoTracking()
            .Where(m => projectIds.Contains(m.ProjectId) && m.IsActive)
            .OrderByDescending(m => m.CreatedAtUtc)
            .Select(m => new { m.Id, m.ProjectId, m.Title, m.Status, m.ScheduledAtUtc })
            .ToListAsync(ct);

        List<PortalMeetingResponse> items = meetings
            .Select(m => new PortalMeetingResponse(
                m.Id, m.ProjectId, m.Title, CrmEnumMapping.ToContracts(m.Status), m.ScheduledAtUtc))
            .ToList();

        return items;
    }
}
