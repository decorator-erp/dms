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

/// <summary>D-12 (A-F3): each lookup is project-scoped on the subject it names — an outsider gets 404.</summary>
public sealed record GetCrmArtifactDocumentsByBriefQuery(Guid BriefId, string UserId)
    : IQuery<IReadOnlyList<CrmArtifactDocumentResponse>>, ICrmProjectScoped
{
    public CrmScopeTarget ScopeTarget => CrmScopeTarget.Brief(BriefId);
}

public sealed record GetCrmArtifactDocumentsBySpaceQuery(Guid SpaceId, string UserId)
    : IQuery<IReadOnlyList<CrmArtifactDocumentResponse>>, ICrmProjectScoped
{
    public CrmScopeTarget ScopeTarget => CrmScopeTarget.Space(SpaceId);
}

public sealed record GetCrmArtifactDocumentsByRequirementItemQuery(Guid RequirementItemId, string UserId)
    : IQuery<IReadOnlyList<CrmArtifactDocumentResponse>>, ICrmProjectScoped
{
    public CrmScopeTarget ScopeTarget => CrmScopeTarget.Item(RequirementItemId);
}

public sealed record GetCrmArtifactDocumentsByMeetingQuery(Guid MeetingId, string UserId)
    : IQuery<IReadOnlyList<CrmArtifactDocumentResponse>>, ICrmProjectScoped
{
    public CrmScopeTarget ScopeTarget => CrmScopeTarget.Meeting(MeetingId);
}
