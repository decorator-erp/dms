using Approvals.Contracts;
using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Crm.Application.Features;
using Crm.Contracts;
using Crm.Contracts.Portal;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Portal;

internal sealed class GetMyPortalRequirementItemsQueryValidator : AbstractValidator<GetMyPortalRequirementItemsQuery>
{
    public GetMyPortalRequirementItemsQueryValidator()
    {
        RuleFor(x => x.PageNumber).InclusiveBetween(1, CrmPaging.MaxPageNumber);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status is not null);
    }
}

/// <summary>
/// crm-complete F1 (D5). The caller must be a portal client (403 otherwise); only its own active projects are
/// read (<see cref="IProjectDirectory.GetProjectsByClientAsync"/>), and a project or period filter outside them
/// is 404, never an empty page that would confirm the id exists. Office drafts (Draft and authored by an
/// employee) are not shown — the client sees what was sent to it and what it submitted itself. The latest
/// decision and its comment come from Approvals in one batched read
/// (<see cref="IApprovalRequester.GetLatestForSubjectsAsync"/>): three statements per page in all.
/// </summary>
internal sealed class GetMyPortalRequirementItemsQueryHandler(
    CrmDbContext db, ICrmAccessGuard guard, IProjectDirectory projectDirectory, IApprovalRequester approvals)
    : IQueryHandler<GetMyPortalRequirementItemsQuery, PagedResult<PortalRequirementItemResponse>>
{
    public async Task<Result<PagedResult<PortalRequirementItemResponse>>> HandleAsync(
        GetMyPortalRequirementItemsQuery query, CancellationToken ct = default)
    {
        Result<CrmActor> actor = await guard.ResolveActorAsync(query.AppUserId, ct);
        if (actor.IsFailure)
        {
            return Result.Failure<PagedResult<PortalRequirementItemResponse>>(actor.Error);
        }

        if (actor.Value!.Kind != CrmActorKind.Client)
        {
            return Result.Failure<PagedResult<PortalRequirementItemResponse>>(CrmPortalErrors.NotAClient);
        }

        var projects = (await projectDirectory.GetProjectsByClientAsync(actor.Value.ClientId!.Value, ct))
            .Where(p => p.IsActive)
            .ToDictionary(p => p.Id, p => p.Name);
        if (query.ProjectId is { } projectId && !projects.ContainsKey(projectId))
        {
            return Result.Failure<PagedResult<PortalRequirementItemResponse>>(CrmPortalErrors.NotFound);
        }

        List<Guid> projectIds = query.ProjectId is { } only ? [only] : [.. projects.Keys];
        if (query.PeriodId is { } periodId
            && !await db.CrmRequirementPeriods.AsNoTracking().AnyAsync(p => p.Id == periodId && projectIds.Contains(p.ProjectId), ct))
        {
            return Result.Failure<PagedResult<PortalRequirementItemResponse>>(CrmPortalErrors.NotFound);
        }

        if (projectIds.Count == 0)
        {
            return new PagedResult<PortalRequirementItemResponse>([], query.PageNumber, query.PageSize, 0);
        }

        IQueryable<CrmRequirementItem> items = db.CrmRequirementItems.AsNoTracking()
            .Where(i => projectIds.Contains(i.ProjectId) && i.IsActive
                && (i.ApprovalStatus != Crm.Domain.ApprovalState.Draft || i.CreatedByContactId != null));
        if (query.PeriodId is { } period)
        {
            items = items.Where(i => i.PeriodId == period);
        }

        if (query.Status is { } status)
        {
            Crm.Domain.ApprovalState domainStatus = CrmEnumMapping.ToDomain(status);
            items = items.Where(i => i.ApprovalStatus == domainStatus);
        }

        int total = await items.CountAsync(ct);
        List<CrmRequirementItem> page = await items
            .OrderByDescending(i => i.CreatedAtUtc).ThenBy(i => i.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .ToListAsync(ct);

        Dictionary<Guid, ApprovalSnapshot> latest = page.Count == 0
            ? []
            : (await approvals.GetLatestForSubjectsAsync(ApprovalSubjectType.RequirementItem, [.. page.Select(i => i.Id)], ct))
                .ToDictionary(a => a.SubjectId);

        Guid? me = actor.Value.ContactId;
        List<PortalRequirementItemResponse> rows = [.. page.Select(i =>
        {
            ApprovalSnapshot? decision = latest.GetValueOrDefault(i.Id);
            return new PortalRequirementItemResponse(
                i.Id, i.ProjectId, projects.GetValueOrDefault(i.ProjectId) ?? string.Empty, i.PeriodId,
                CrmEnumMapping.ToContracts(i.Category), i.Title, i.Description, i.Room, CrmEnumMapping.ToContracts(i.Priority),
                i.SpaceTypeId, CrmEnumMapping.ToContracts(i.ApprovalStatus), me is not null && i.CreatedByContactId == me,
                decision?.Decision is { } d ? (PortalApprovalDecision)(int)d : null,
                decision?.Decision is null ? null : decision.Comment,
                decision?.DecidedAtUtc?.UtcDateTime, i.CreatedAtUtc);
        })];

        return new PagedResult<PortalRequirementItemResponse>(rows, query.PageNumber, query.PageSize, total);
    }
}
