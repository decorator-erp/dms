using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
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
/// </summary>
public sealed record ProjectResponse(
    Guid Id, string Code, string Name, string? Description, ProjectStatus Status, WorkflowType WorkflowType,
    string Address, string? Notes, decimal ProgressPercentage,
    DateTime? EstimatedStartDate, DateTime? EstimatedEndDate, DateTime? ActualStartDate, DateTime? ActualEndDate,
    Guid ClientId, Guid OwnerId, bool IsActive, int StageCount);

public sealed record CreateProjectCommand(
    string Code, string Name, string Address, WorkflowType WorkflowType, Guid ClientId,
    string? Description = null, string? Notes = null,
    DateTime? EstimatedStartDate = null, DateTime? EstimatedEndDate = null,
    Guid? StageTemplateId = null) : ICommand<Guid>;

public sealed record UpdateProjectCommand(
    Guid ProjectId, string UserId, string? Name = null, string? Description = null, string? Address = null,
    string? Notes = null, DateTime? EstimatedStartDate = null, DateTime? EstimatedEndDate = null)
    : ICommand, IProjectScoped;

public sealed record ChangeProjectStatusCommand(Guid ProjectId, string UserId, ProjectStatus Status)
    : ICommand, IProjectScoped;

public sealed record SetProjectActiveCommand(Guid ProjectId, string UserId, bool IsActive)
    : ICommand, IProjectScoped;

public sealed record DeleteProjectCommand(Guid ProjectId, string UserId) : ICommand, IProjectScoped;

public sealed record GetProjectQuery(Guid ProjectId, string UserId) : IQuery<ProjectResponse>, IProjectScoped;

public sealed record GetProjectsQuery(int PageNumber, int PageSize, string UserId, string? NameFilter = null)
    : IQuery<PagedResult<ProjectResponse>>;

/// <summary>B2-be: the project page's one-shot read, <c>GET /api/projects/{projectId}/details</c>. Project-scoped
/// like <see cref="GetProjectQuery"/> (an outsider gets 404 from the scope decorator) and, like it, returns an
/// inactive project with <c>IsActive</c> false. A fixed number of SQL statements whatever the stage count.</summary>
public sealed record GetProjectDetailsQuery(Guid ProjectId, string UserId)
    : IQuery<ProjectDetailsResponse>, IProjectScoped;

/// <summary><see cref="UnstagedTasks"/> are the tasks with no stage (a FreeStyle project's). <see cref="TaskCounts"/>
/// counts every task of the project by status, only the statuses that occur. <see cref="Gates"/> summarises
/// every gate of the project, stage-owned and task-owned.</summary>
public sealed record ProjectDetailsResponse(
    ProjectDetailsProjectResponse Project,
    IReadOnlyList<ProjectStageDetailsResponse> Stages,
    IReadOnlyList<ProjectTaskResponse> UnstagedTasks,
    IReadOnlyList<TaskStatusCount> TaskCounts,
    GateSummary Gates);

/// <summary><see cref="ProjectResponse"/>'s fields plus <see cref="ClientName"/>, the client's display name read
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
