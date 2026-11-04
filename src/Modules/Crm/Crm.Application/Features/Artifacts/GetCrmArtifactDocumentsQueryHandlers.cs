using BuildingBlocks.Messaging;
using Crm.Contracts.Artifacts;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Artifacts;

/// <summary>L-4: every by-subject artifact list returns at most <see cref="MaxRows"/> links, oldest first
/// (ties broken by id), so the answer is bounded and stable.</summary>
internal static class CrmArtifactLists
{
    public const int MaxRows = 200;
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
            .Take(CrmArtifactLists.MaxRows)
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
            .Take(CrmArtifactLists.MaxRows)
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
            .Take(CrmArtifactLists.MaxRows)
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
            .Take(CrmArtifactLists.MaxRows)
            .Select(a => new CrmArtifactDocumentResponse(
                a.Id, a.DocumentId, a.BriefId, a.SpaceId, a.RequirementItemId, a.MeetingId,
                a.AddedByEmployeeId, a.AddedByContactId))
            .ToListAsync(ct);

        return artifacts;
    }
}
