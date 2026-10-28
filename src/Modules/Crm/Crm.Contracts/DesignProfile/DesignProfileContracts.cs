using BuildingBlocks.Messaging;

namespace Crm.Contracts.DesignProfile;

/// <summary>The office-facing read of a client's accumulated design preferences, gated by
/// <c>crm.design_profile.read</c>. Deliberately excludes <c>CrmDesignBrief.ExpectedBudget</c> — its
/// own doc comment says it is "office-side only — never exposed to designers".</summary>
public sealed record ClientDesignProfileResponse(
    Guid ProjectId, string? PreferredStyle, string? PreferredColors, FurnitureStatus? FurnitureStatus,
    HiringReason HiringReasons, string? ArchitecturalRequests, string? OtherNeeds);

/// <summary>C-2 (LC-M1): project-scoped like the brief it reads; the latest brief on the project wins.</summary>
public sealed record GetClientDesignProfileQuery(Guid ProjectId, string UserId) : IQuery<ClientDesignProfileResponse>, ICrmProjectScoped
{
    CrmScopeTarget ICrmProjectScoped.ScopeTarget => CrmScopeTarget.Project(ProjectId);
}
