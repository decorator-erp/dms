namespace Crm.Contracts;

/// <summary>What a project-scoped CRM message names, from which <c>CrmProjectScopeDecorator</c> resolves
/// the project it belongs to.</summary>
public enum CrmScopeTargetKind
{
    Project = 1,
    Stage = 2,
    Brief = 3,
    BriefSpaceEntry = 4,
}

public sealed record CrmScopeTarget(CrmScopeTargetKind Kind, Guid Id)
{
    public static CrmScopeTarget Project(Guid projectId) => new(CrmScopeTargetKind.Project, projectId);
    public static CrmScopeTarget Stage(Guid stageId) => new(CrmScopeTargetKind.Stage, stageId);
    public static CrmScopeTarget Brief(Guid briefId) => new(CrmScopeTargetKind.Brief, briefId);
    public static CrmScopeTarget BriefSpaceEntry(Guid entryId) => new(CrmScopeTargetKind.BriefSpaceEntry, entryId);
}

/// <summary>
/// Marker for an office CRM message whose subject belongs to one project — the design brief and its
/// brief-space ledger. <c>CrmProjectScopeDecorator</c> resolves <see cref="ScopeTarget"/> to its
/// <c>ProjectId</c> from CRM's own tables (or, for a stage, <c>IProjectDirectory</c>) and checks
/// <c>Projects.Contracts.IProjectAccessGuard</c> before the handler runs; any failure is a 404, per
/// <c>CLAUDE.md</c>'s authorization order (route claims 403 → scope 404 → aggregate state 409).
/// Mirrors <c>Projects.Contracts.IProjectScoped</c>, which can name only a project id.
///
/// <para><see cref="UserId"/> travels on the message, filled by the endpoint from
/// <c>ICurrentUser</c> — never from the request body. Portal (client) messages stay
/// <see cref="ICrmScoped"/>; their own handlers check the client owns the project.</para>
/// </summary>
public interface ICrmProjectScoped
{
    string UserId { get; }
    CrmScopeTarget ScopeTarget { get; }
}
