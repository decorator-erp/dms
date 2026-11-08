using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Projects.Contracts.Grants;
using Projects.Contracts.Tasks;

namespace Projects.Contracts.Projects;

/// <summary>
/// Command/query wrapper types and DTOs for Projects' own HTTP surface — same circular-reference reason
/// established at Identity's G1-07: <c>Projects.Infrastructure</c>'s endpoints need to name these exact
/// types, and <c>Projects.Application</c> already has a real <c>ProjectReference</c> to
/// <c>Projects.Infrastructure</c>. <c>OwnerId</c> is resolved server-side from the caller's identity via
/// <c>Hr.Contracts.IHrDirectory</c> — never a command field, so it can't be spoofed from the request body.
/// <c>ClientId</c> stays an unvalidated <c>Guid</c> everywhere (CRM absent).
///
/// <para><see cref="StageCount"/> is how many stages the project has (0 until a template or a confirmed scope
/// of work creates them).</para>
///
/// <para><see cref="MyLevel"/> is the caller's own level on the project (1 Viewer, 2 Editor, 3 Manager), read from
/// <see cref="IProjectAccessLevelReader"/>, the same source the scope decorator enforces with — so a command the
/// level does not reach is the 403 <c>projects.InsufficientLevel</c> the UI can predict.</para>
///
/// <para>P1-A: <see cref="MyAccess"/> says where that level comes from (membership, a grant or
/// <c>project.read_all</c>) and, for a grant, which areas and stages it covers and when it ends. <see cref="MyLevel"/>
/// equals <c>MyAccess.Level</c>.</para>
///
/// <para>S6: <see cref="ClientName"/> (the client login's display name) and <see cref="OwnerName"/> (the owner
/// employee's full name) are read in batches for the whole page; null when either cannot be resolved.</para>
/// </summary>
public sealed record ProjectResponse(
    Guid Id, string Code, string Name, string? Description, ProjectStatus Status, WorkflowType WorkflowType,
    string Address, string? Notes, decimal ProgressPercentage,
    DateTime? EstimatedStartDate, DateTime? EstimatedEndDate, DateTime? ActualStartDate, DateTime? ActualEndDate,
    Guid ClientId, Guid OwnerId, bool IsActive, int StageCount, ProjectMemberAccess MyLevel,
    ProjectAccessResponse MyAccess, string? ClientName, string? OwnerName);

public sealed record CreateProjectCommand(
    string Code, string Name, string Address, WorkflowType WorkflowType, Guid ClientId,
    string? Description = null, string? Notes = null,
    DateTime? EstimatedStartDate = null, DateTime? EstimatedEndDate = null,
    Guid? StageTemplateId = null) : ICommand<Guid>;

public sealed record UpdateProjectCommand(
    Guid ProjectId, string UserId, string? Name = null, string? Description = null, string? Address = null,
    string? Notes = null, DateTime? EstimatedStartDate = null, DateTime? EstimatedEndDate = null)
    : ICommand, IProjectScoped, IRequiresProjectManager;

public sealed record ChangeProjectStatusCommand(Guid ProjectId, string UserId, ProjectStatus Status)
    : ICommand, IProjectScoped, IRequiresProjectManager;

public sealed record SetProjectActiveCommand(Guid ProjectId, string UserId, bool IsActive)
    : ICommand, IProjectScoped, IRequiresProjectManager;

public sealed record DeleteProjectCommand(Guid ProjectId, string UserId) : ICommand, IProjectScoped, IRequiresProjectManager;

public sealed record GetProjectQuery(Guid ProjectId, string UserId) : IQuery<ProjectResponse>, IProjectScoped;

/// <summary>E8 filters, all ANDed and all inside project scope: <see cref="Mine"/> (projects the caller owns
/// or is a member of), <see cref="ClientId"/>, and <see cref="Search"/> (name or code, case-insensitive,
/// trimmed). <see cref="NameFilter"/> is the older case-sensitive name filter, kept as it was.</summary>
public sealed record GetProjectsQuery(
    int PageNumber, int PageSize, string UserId, string? NameFilter = null, bool Mine = false,
    Guid? ClientId = null, string? Search = null)
    : IQuery<PagedResult<ProjectResponse>>;

/// <summary>B2-be: the project page's one-shot read, <c>GET /api/projects/{projectId}/details</c>. Project-scoped
/// like <see cref="GetProjectQuery"/> (an outsider gets 404 from the scope decorator) and, like it, returns an
/// inactive project with <c>IsActive</c> false. A fixed number of SQL statements whatever the stage count.</summary>
public sealed record GetProjectDetailsQuery(Guid ProjectId, string UserId)
    : IQuery<ProjectDetailsResponse>, IProjectScoped;

/// <summary><see cref="UnstagedTasks"/> are the tasks with no stage (a FreeStyle project's). <see cref="TaskCounts"/>
/// counts every task of the project by status, only the statuses that occur. <see cref="Gates"/> summarises
/// every gate of the project, stage-owned and task-owned. <see cref="MyLevel"/> is the caller's level, as on
/// <see cref="ProjectResponse.MyLevel"/>.</summary>
public sealed record ProjectDetailsResponse(
    ProjectDetailsProjectResponse Project,
    IReadOnlyList<ProjectStageDetailsResponse> Stages,
    IReadOnlyList<ProjectTaskResponse> UnstagedTasks,
    IReadOnlyList<TaskStatusCount> TaskCounts,
    GateSummary Gates,
    ProjectMemberAccess MyLevel,
    ProjectAccessResponse MyAccess);

/// <summary><see cref="ProjectResponse"/>'s fields (except <c>MyLevel</c>, which sits on
/// <see cref="ProjectDetailsResponse"/> itself) plus <see cref="ClientName"/>, the client's display name read
/// through <c>Crm.Contracts.IClientDirectory</c>; null when the client cannot be resolved.</summary>
public sealed record ProjectDetailsProjectResponse(
    Guid Id, string Code, string Name, string? Description, ProjectStatus Status, WorkflowType WorkflowType,
    string Address, string? Notes, decimal ProgressPercentage,
    DateTime? EstimatedStartDate, DateTime? EstimatedEndDate, DateTime? ActualStartDate, DateTime? ActualEndDate,
    Guid ClientId, string? ClientName, Guid OwnerId, bool IsActive, int StageCount);

/// <summary>A stage with its tasks (ordered), their counts by status, and the summary of the gates owned by the
/// stage or by one of its tasks. <see cref="StageId"/> is the template stage it was made from, if any.</summary>
public sealed record ProjectStageDetailsResponse(
    Guid Id, Guid ProjectId, Guid? StageId, string Name, string? Description, int Order, StageStatus Status,
    decimal ProgressPercent, int CompletedTasks, int TotalTasks, decimal? Cost,
    DateTime? EstimatedStartDate, DateTime? EstimatedEndDate, DateTime? ActualStartDate, DateTime? ActualEndDate,
    string? Notes,
    IReadOnlyList<ProjectTaskResponse> Tasks,
    IReadOnlyList<TaskStatusCount> TaskCounts,
    GateSummary Gates);

public sealed record TaskStatusCount(ProjectTaskStatus Status, int Count);

public sealed record GateSummary(int Total, int Pending, int Satisfied, int Rejected);
