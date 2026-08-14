using BuildingBlocks.Messaging;
using Crm.Contracts.Artifacts;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Application.Features.Artifacts;

internal sealed class GetCrmArtifactDocumentsByBriefQueryHandler(CrmDbContext db)
    : IQueryHandler<GetCrmArtifactDocumentsByBriefQuery, IReadOnlyList<CrmArtifactDocumentResponse>>
{
    public async Task<Result<IReadOnlyList<CrmArtifactDocumentResponse>>> HandleAsync(
        GetCrmArtifactDocumentsByBriefQuery query, CancellationToken ct = default)
    {
        List<CrmArtifactDocumentResponse> artifacts = await db.CrmArtifactDocuments.AsNoTracking()
            .Where(a => a.BriefId == query.BriefId)
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
            .Select(a => new CrmArtifactDocumentResponse(
                a.Id, a.DocumentId, a.BriefId, a.SpaceId, a.RequirementItemId, a.MeetingId,
                a.AddedByEmployeeId, a.AddedByContactId))
            .ToListAsync(ct);

        return artifacts;
    }
}
