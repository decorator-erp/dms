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
    Guid? AddedByEmployeeId, Guid? AddedByContactId, string UserId) : ICommand<Guid>, ICrmProjectScoped
{
    /// <summary>FINDING-P1-13: scoped on the one subject the link names (exactly one is set; the handler
    /// rejects anything else). None named resolves to no project — a 404.</summary>
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => (BriefId, SpaceId, RequirementItemId, MeetingId) switch
    {
        ({ } briefId, _, _, _) => CrmScopeTarget.Brief(briefId),
        (_, { } spaceId, _, _) => CrmScopeTarget.Space(spaceId),
        (_, _, { } itemId, _) => CrmScopeTarget.Item(itemId),
        (_, _, _, { } meetingId) => CrmScopeTarget.Meeting(meetingId),
        _ => CrmScopeTarget.Brief(Guid.Empty),
    };
}

/// <summary>D-12 (A-F3): each lookup is project-scoped on the subject it names — an outsider gets 404.</summary>
public sealed record GetCrmArtifactDocumentsByBriefQuery(Guid BriefId, string UserId)
    : IQuery<IReadOnlyList<CrmArtifactDocumentResponse>>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Brief(BriefId);
}

public sealed record GetCrmArtifactDocumentsBySpaceQuery(Guid SpaceId, string UserId)
    : IQuery<IReadOnlyList<CrmArtifactDocumentResponse>>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Space(SpaceId);
}

public sealed record GetCrmArtifactDocumentsByRequirementItemQuery(Guid RequirementItemId, string UserId)
    : IQuery<IReadOnlyList<CrmArtifactDocumentResponse>>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Item(RequirementItemId);
}

public sealed record GetCrmArtifactDocumentsByMeetingQuery(Guid MeetingId, string UserId)
    : IQuery<IReadOnlyList<CrmArtifactDocumentResponse>>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Meeting(MeetingId);
}
