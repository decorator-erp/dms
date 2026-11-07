using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Artifacts;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Artifacts;

/// <summary>L-4: every by-subject artifact list returns at most <see cref="MaxRows"/> links, oldest first
/// (ties broken by id), so the answer is bounded and stable.</summary>
internal static class CrmArtifactLists
{
    public const int MaxRows = 200;
}

// crm-complete F7: the by-subject lists page (pageSize <= 200, the old cap, which stays the default).

internal sealed class GetCrmArtifactDocumentsByBriefQueryValidator : AbstractValidator<GetCrmArtifactDocumentsByBriefQuery>
{
    public GetCrmArtifactDocumentsByBriefQueryValidator()
    {
        RuleFor(x => x.PageNumber).InclusiveBetween(1, CrmPaging.MaxPageNumber);
        RuleFor(x => x.PageSize).InclusiveBetween(1, CrmArtifactLists.MaxRows);
    }
}

internal sealed class GetCrmArtifactDocumentsBySpaceQueryValidator : AbstractValidator<GetCrmArtifactDocumentsBySpaceQuery>
{
    public GetCrmArtifactDocumentsBySpaceQueryValidator()
    {
        RuleFor(x => x.PageNumber).InclusiveBetween(1, CrmPaging.MaxPageNumber);
        RuleFor(x => x.PageSize).InclusiveBetween(1, CrmArtifactLists.MaxRows);
    }
}

internal sealed class GetCrmArtifactDocumentsByRequirementItemQueryValidator : AbstractValidator<GetCrmArtifactDocumentsByRequirementItemQuery>
{
    public GetCrmArtifactDocumentsByRequirementItemQueryValidator()
    {
        RuleFor(x => x.PageNumber).InclusiveBetween(1, CrmPaging.MaxPageNumber);
        RuleFor(x => x.PageSize).InclusiveBetween(1, CrmArtifactLists.MaxRows);
    }
}

internal sealed class GetCrmArtifactDocumentsByMeetingQueryValidator : AbstractValidator<GetCrmArtifactDocumentsByMeetingQuery>
{
    public GetCrmArtifactDocumentsByMeetingQueryValidator()
    {
        RuleFor(x => x.PageNumber).InclusiveBetween(1, CrmPaging.MaxPageNumber);
        RuleFor(x => x.PageSize).InclusiveBetween(1, CrmArtifactLists.MaxRows);
    }
}

internal sealed class GetCrmArtifactDocumentsByBriefQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmArtifactDocumentsByBriefQuery, IReadOnlyList<CrmArtifactDocumentResponse>>
{
    public async Task<Result<IReadOnlyList<CrmArtifactDocumentResponse>>> HandleAsync(
        GetCrmArtifactDocumentsByBriefQuery query, CancellationToken ct = default)
    {
        List<CrmArtifactDocumentResponse> artifacts = await db.CrmArtifactDocuments.AsNoTracking()
            .Where(a => a.BriefId == query.BriefId)
            .OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(a => new CrmArtifactDocumentResponse(
                a.Id, a.DocumentId, a.BriefId, a.SpaceId, a.RequirementItemId, a.MeetingId,
                a.AddedByEmployeeId, a.AddedByContactId))
            .ToListAsync(ct);

        return artifacts;
    }
}

internal sealed class GetCrmArtifactDocumentsBySpaceQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmArtifactDocumentsBySpaceQuery, IReadOnlyList<CrmArtifactDocumentResponse>>
{
    public async Task<Result<IReadOnlyList<CrmArtifactDocumentResponse>>> HandleAsync(
        GetCrmArtifactDocumentsBySpaceQuery query, CancellationToken ct = default)
    {
        List<CrmArtifactDocumentResponse> artifacts = await db.CrmArtifactDocuments.AsNoTracking()
            .Where(a => a.SpaceId == query.SpaceId)
            .OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(a => new CrmArtifactDocumentResponse(
                a.Id, a.DocumentId, a.BriefId, a.SpaceId, a.RequirementItemId, a.MeetingId,
                a.AddedByEmployeeId, a.AddedByContactId))
            .ToListAsync(ct);

        return artifacts;
    }
}

internal sealed class GetCrmArtifactDocumentsByRequirementItemQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmArtifactDocumentsByRequirementItemQuery, IReadOnlyList<CrmArtifactDocumentResponse>>
{
    public async Task<Result<IReadOnlyList<CrmArtifactDocumentResponse>>> HandleAsync(
        GetCrmArtifactDocumentsByRequirementItemQuery query, CancellationToken ct = default)
    {
        List<CrmArtifactDocumentResponse> artifacts = await db.CrmArtifactDocuments.AsNoTracking()
            .Where(a => a.RequirementItemId == query.RequirementItemId)
            .OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(a => new CrmArtifactDocumentResponse(
                a.Id, a.DocumentId, a.BriefId, a.SpaceId, a.RequirementItemId, a.MeetingId,
                a.AddedByEmployeeId, a.AddedByContactId))
            .ToListAsync(ct);

        return artifacts;
    }
}

internal sealed class GetCrmArtifactDocumentsByMeetingQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmArtifactDocumentsByMeetingQuery, IReadOnlyList<CrmArtifactDocumentResponse>>
{
    public async Task<Result<IReadOnlyList<CrmArtifactDocumentResponse>>> HandleAsync(
        GetCrmArtifactDocumentsByMeetingQuery query, CancellationToken ct = default)
    {
        List<CrmArtifactDocumentResponse> artifacts = await db.CrmArtifactDocuments.AsNoTracking()
            .Where(a => a.MeetingId == query.MeetingId)
            .OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(a => new CrmArtifactDocumentResponse(
                a.Id, a.DocumentId, a.BriefId, a.SpaceId, a.RequirementItemId, a.MeetingId,
                a.AddedByEmployeeId, a.AddedByContactId))
            .ToListAsync(ct);

        return artifacts;
    }
}

/// <summary>crm-complete F7: route claim (403) → project scope of the link's subject (404) → the linker or a
/// <c>crm.artifacts.manage</c> holder (403). Removes the link only; the document stays in Documents.</summary>
internal sealed class DeleteCrmArtifactDocumentCommandHandler(CrmDbContext db, ICrmAccessGuard guard, BuildingBlocks.ICurrentUser currentUser)
    : ICommandHandler<DeleteCrmArtifactDocumentCommand>
{
    public async Task<Result> HandleAsync(DeleteCrmArtifactDocumentCommand command, CancellationToken ct = default)
    {
        CrmArtifactDocument? link = await db.CrmArtifactDocuments.FirstOrDefaultAsync(a => a.Id == command.LinkId, ct);
        if (link is null)
        {
            return CrmArtifactErrors.LinkNotFound;
        }

        if (!currentUser.HasPermission(CrmPermissions.Artifacts.Manage))
        {
            Result<CrmActor> actor = await guard.ResolveActorAsync(command.UserId, ct);
            if (actor.IsFailure || actor.Value!.EmployeeId is null || actor.Value.EmployeeId != link.AddedByEmployeeId)
            {
                return CrmArtifactErrors.NotLinker;
            }
        }

        db.CrmArtifactDocuments.Remove(link);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return CrmArtifactErrors.LinkNotFound;
        }

        return Result.Success();
    }
}
