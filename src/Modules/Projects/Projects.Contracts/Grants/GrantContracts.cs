using BuildingBlocks.Messaging;

namespace Projects.Contracts.Grants;

/// <summary>
/// P1-A: one project access grant, as <c>GET /api/projects/{projectId}/access-grants</c> lists it. A grant gives
/// <see cref="EmployeeId"/>, who is not on the project, <see cref="Level"/> on <see cref="Areas"/> of one project,
/// limited to <see cref="StageIds"/> (empty: the whole project), from <see cref="StartsAtUtc"/> until
/// <see cref="ExpiresAtUtc"/> (null: no end). <see cref="Status"/> is computed at read time. <see cref="EmployeeName"/>
/// is null when HR has no profile for the id.
/// </summary>
public sealed record ProjectAccessGrantResponse(
    Guid Id, Guid ProjectId, Guid EmployeeId, string? EmployeeName, ProjectMemberAccess Level, ProjectAccessArea Areas,
    IReadOnlyList<Guid> StageIds, DateTime StartsAtUtc, DateTime? ExpiresAtUtc, string Reason,
    Guid GrantedById, DateTime GrantedAtUtc, DateTime? RevokedAtUtc, Guid? RevokedById, string? RevokeReason,
    ProjectAccessGrantStatus Status);

/// <summary>P1-A: one of the caller's own grants (<c>GET /api/me/access-grants</c>, "Shared with me"): only Active and
/// Upcoming ones, with the project's code and name.</summary>
public sealed record MyAccessGrantResponse(
    Guid GrantId, Guid ProjectId, string ProjectCode, string ProjectName, ProjectMemberAccess Level,
    ProjectAccessArea Areas, IReadOnlyList<Guid> StageIds, DateTime StartsAtUtc, DateTime? ExpiresAtUtc, string Reason,
    ProjectAccessGrantStatus Status);

/// <summary>
/// P1-A: how the caller reaches a project, appended to <see cref="Projects.ProjectResponse"/> and
/// <see cref="Projects.ProjectDetailsResponse"/>. <see cref="Source"/> is <c>"ReadAll"</c> (<c>project.read_all</c>:
/// Manager on every area), <c>"Member"</c> (owner, stage team, member row or task assignee: the membership level on
/// every area) or <c>"Grant"</c> (only an active grant: its level, areas, stages and expiry). Membership wins over a
/// grant. <see cref="StageIds"/> is empty unless a grant limits the caller to some stages.
/// </summary>
public sealed record ProjectAccessResponse(
    string Source, ProjectMemberAccess Level, ProjectAccessArea Areas, IReadOnlyList<Guid> StageIds, DateTime? ExpiresAtUtc)
{
    public const string ReadAllSource = "ReadAll";
    public const string MemberSource = "Member";
    public const string GrantSource = "Grant";
}

/// <summary>
/// P1-A: these messages are deliberately NOT <see cref="IProjectScoped"/>. Who may manage grants is not a project
/// level the scope decorator knows: it is a holder of <see cref="ProjectsPermissions.Projects.ManageAccess"/>, or a
/// Manager <i>by membership</i> (never through a grant, never through <c>project.read_all</c> alone). The handlers
/// check it themselves, in the repo's order: an unreachable project is 404, then a caller who may not manage grants
/// is 403 <c>projects.access_grants.NotAllowed</c>, then the grant's state is 409.
/// </summary>
public sealed record GetProjectAccessGrantsQuery(Guid ProjectId, string UserId)
    : IQuery<IReadOnlyList<ProjectAccessGrantResponse>>;

/// <summary>P1-A. <see cref="StartsAtUtc"/> defaults to now; <see cref="ExpiresAtUtc"/> null means no end and must
/// otherwise be later than the start and than now. <see cref="StageIds"/> must all be stages of the project (empty:
/// the whole project). <see cref="Reason"/> is required, at most 500 characters.</summary>
public sealed record CreateProjectAccessGrantCommand(
    Guid ProjectId, string UserId, Guid EmployeeId, ProjectMemberAccess Level, ProjectAccessArea Areas,
    IReadOnlyList<Guid> StageIds, DateTime? StartsAtUtc, DateTime? ExpiresAtUtc, string Reason) : ICommand<Guid>;

/// <summary>P1-A: replaces the editable fields of an Upcoming or Active grant; a Revoked or Expired one is 409
/// <c>projects.access_grants.NotEditable</c> (a new grant is made instead, so history stays honest).</summary>
public sealed record UpdateProjectAccessGrantCommand(
    Guid ProjectId, string UserId, Guid GrantId, ProjectMemberAccess Level, ProjectAccessArea Areas,
    IReadOnlyList<Guid> StageIds, DateTime? ExpiresAtUtc, string Reason) : ICommand;

/// <summary>P1-A: final. Takes effect on the grantee's next request: nothing from a grant is in a token.</summary>
public sealed record RevokeProjectAccessGrantCommand(Guid ProjectId, string UserId, Guid GrantId, string? Reason)
    : ICommand;

/// <summary>P1-A: the caller's own Active and Upcoming grants, soonest-ending first.</summary>
public sealed record GetMyAccessGrantsQuery(string UserId) : IQuery<IReadOnlyList<MyAccessGrantResponse>>;
