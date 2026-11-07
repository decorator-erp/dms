using BuildingBlocks.Messaging;

namespace Crm.Contracts.Spaces;

public sealed record CrmSpaceTypeResponse(
    Guid Id, string Code, string NameEn, string NameAr, int SortOrder, bool IsActive);

public sealed record CrmSpaceResponse(
    Guid Id, Guid ProjectId, Guid SpaceTypeId, string Name, string? Description, int SortOrder, bool IsActive);

public sealed record CreateCrmSpaceTypeCommand(string Code, string NameEn, string NameAr, int SortOrder = 0)
    : ICommand<Guid>;

public sealed record UpdateCrmSpaceTypeCommand(Guid SpaceTypeId, string NameEn, string NameAr, int SortOrder)
    : ICommand;

public sealed record SetCrmSpaceTypeActiveCommand(Guid SpaceTypeId, bool IsActive) : ICommand;

public sealed record CreateCrmSpaceCommand(
    Guid ProjectId, Guid SpaceTypeId, string Name, string UserId, int SortOrder = 0, string? Description = null)
    : ICommand<Guid>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Project(ProjectId);
}

public sealed record UpdateCrmSpaceCommand(Guid SpaceId, Guid SpaceTypeId, string Name, string? Description, string UserId)
    : ICommand, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Space(SpaceId);
}

public sealed record SetCrmSpaceActiveCommand(Guid SpaceId, bool IsActive, string UserId) : ICommand, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Space(SpaceId);
}

/// <summary>crm-complete F5: the project's full space order; exactly its spaces, each once (else 400).</summary>
public sealed record ReorderCrmSpacesCommand(Guid ProjectId, IReadOnlyList<Guid> SpaceIds, string UserId) : ICommand, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Project(ProjectId);
}

/// <summary>F5 (D4): hard delete only when nothing references the space (409 <c>crm.spaces.InUse</c>).</summary>
public sealed record DeleteCrmSpaceCommand(Guid SpaceId, string UserId) : ICommand, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Space(SpaceId);
}

public sealed record GetCrmSpaceTypesQuery : IQuery<IReadOnlyList<CrmSpaceTypeResponse>>;

public sealed record GetCrmSpacesByProjectQuery(Guid ProjectId, string UserId) : IQuery<IReadOnlyList<CrmSpaceResponse>>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Project(ProjectId);
}
