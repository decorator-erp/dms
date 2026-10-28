using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Crm.Contracts;
using Crm.Contracts.Portal;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
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
/// <summary>B-15 (MT-N7): same paging rules as the office lists.</summary>
internal sealed class GetMyPortalMeetingsQueryValidator : AbstractValidator<GetMyPortalMeetingsQuery>
{
    public GetMyPortalMeetingsQueryValidator()
    {
        RuleFor(x => x.PageNumber).InclusiveBetween(1, CrmPaging.MaxPageNumber);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

internal sealed class GetMyPortalMeetingsQueryHandler(CrmDbContext db, ICrmAccessGuard guard, IProjectDirectory projectDirectory)
    : IQueryHandler<GetMyPortalMeetingsQuery, PagedResult<PortalMeetingResponse>>
{
    public async Task<Result<PagedResult<PortalMeetingResponse>>> HandleAsync(
        GetMyPortalMeetingsQuery query, CancellationToken ct = default)
    {
        Result<CrmActor> actor = await guard.ResolveActorAsync(query.AppUserId, ct);
        if (actor.IsFailure)
        {
            return Result.Failure<PagedResult<PortalMeetingResponse>>(actor.Error);
        }

        if (actor.Value!.Kind != CrmActorKind.Client)
        {
            return Result.Failure<PagedResult<PortalMeetingResponse>>(CrmPortalErrors.NotAClient);
        }

        IReadOnlyList<ProjectSummary> projects = await projectDirectory.GetProjectsByClientAsync(actor.Value.ClientId!.Value, ct);
        if (projects.Count == 0)
        {
            return new PagedResult<PortalMeetingResponse>([], query.PageNumber, query.PageSize, 0);
        }

        var projectIds = projects.Select(p => p.Id).ToList();
        IQueryable<CrmMeeting> matching = db.CrmMeetings.AsNoTracking()
            .Where(m => projectIds.Contains(m.ProjectId) && m.IsActive);
        int total = await matching.CountAsync(ct);
        var meetings = await matching
            .OrderByDescending(m => m.CreatedAtUtc).ThenBy(m => m.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(m => new { m.Id, m.ProjectId, m.Title, m.Status, m.ScheduledAtUtc })
            .ToListAsync(ct);

        var items = meetings
            .Select(m => new PortalMeetingResponse(
                m.Id, m.ProjectId, m.Title, CrmEnumMapping.ToContracts(m.Status), m.ScheduledAtUtc))
            .ToList();

        return new PagedResult<PortalMeetingResponse>(items, query.PageNumber, query.PageSize, total);
    }
}
