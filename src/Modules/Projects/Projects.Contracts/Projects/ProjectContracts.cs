using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;

namespace Projects.Contracts.Projects;

/// <summary>
/// Command/query wrapper types and DTOs for Projects' own HTTP surface — same circular-reference reason
/// established at Identity's G1-07: <c>Projects.Infrastructure</c>'s endpoints need to name these exact
/// types, and <c>Projects.Application</c> already has a real <c>ProjectReference</c> to
/// <c>Projects.Infrastructure</c>. <c>OwnerId</c> is resolved server-side from the caller's identity via
/// <c>Hr.Contracts.IHrDirectory</c> — never a command field, so it can't be spoofed from the request body.
/// <c>ClientId</c> stays an unvalidated <c>Guid</c> everywhere (CRM absent).
/// </summary>
public sealed record ProjectResponse(
    Guid Id, string Code, string Name, string? Description, ProjectStatus Status, WorkflowType WorkflowType,
    string Address, string? Notes, decimal ProgressPercentage,
    DateTime? EstimatedStartDate, DateTime? EstimatedEndDate, DateTime? ActualStartDate, DateTime? ActualEndDate,
    Guid ClientId, Guid OwnerId, bool IsActive);

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
