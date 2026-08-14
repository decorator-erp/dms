using BuildingBlocks.Messaging;

namespace Crm.Contracts.Artifacts;

public sealed record CrmArtifactDocumentResponse(
    Guid Id, Guid DocumentId, Guid? BriefId, Guid? SpaceId, Guid? RequirementItemId, Guid? MeetingId,
    Guid? AddedByEmployeeId, Guid? AddedByContactId);

/// <summary><see cref="DocumentId"/> must name an existing <c>documents.project_documents</c> row —
/// validated via <c>Documents.Contracts.IDocumentQueries.GetAsync</c> before the row is created. This
/// check is the entire replacement for the FK structural item 1 deliberately drops; an unknown id is
/// rejected here, not by a constraint.</summary>
public sealed record LinkCrmArtifactDocumentCommand(
    Guid DocumentId, Guid? BriefId, Guid? SpaceId, Guid? RequirementItemId, Guid? MeetingId,
    Guid? AddedByEmployeeId, Guid? AddedByContactId) : ICommand<Guid>;

public sealed record GetCrmArtifactDocumentsByBriefQuery(Guid BriefId)
    : IQuery<IReadOnlyList<CrmArtifactDocumentResponse>>;

public sealed record GetCrmArtifactDocumentsBySpaceQuery(Guid SpaceId)
    : IQuery<IReadOnlyList<CrmArtifactDocumentResponse>>;

public sealed record GetCrmArtifactDocumentsByRequirementItemQuery(Guid RequirementItemId)
    : IQuery<IReadOnlyList<CrmArtifactDocumentResponse>>;

public sealed record GetCrmArtifactDocumentsByMeetingQuery(Guid MeetingId)
    : IQuery<IReadOnlyList<CrmArtifactDocumentResponse>>;
