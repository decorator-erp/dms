using BuildingBlocks.Messaging;
using Projects.Contracts.Projects;

namespace Projects.Contracts.Tasks;

public sealed record ProjectTaskResponse(
    Guid Id, Guid ProjectId, Guid? ProjectStageId, string Name, string? Description, int Order,
    ProjectTaskStatus Status, TaskPriority Priority, decimal Weight, Guid? AssignedToId, Guid? DepartmentId,
    DateTime? DueDate, decimal? Cost);

/// <summary><paramref name="EmployeeId"/> is validated via <c>Hr.Contracts.IHrDirectory.GetAsync</c> in
/// the handler (G5-07) — an unknown/inactive employee id is rejected, never silently stored.</summary>
public sealed record AssignProjectTaskCommand(Guid ProjectId, Guid TaskId, string UserId, Guid EmployeeId)
    : ICommand, IProjectScoped;

public sealed record StartProjectTaskCommand(Guid ProjectId, Guid TaskId, string UserId) : ICommand, IProjectScoped;

public sealed record CompleteProjectTaskCommand(Guid ProjectId, Guid TaskId, string UserId) : ICommand, IProjectScoped;

public sealed record RejectProjectTaskCommand(Guid ProjectId, Guid TaskId, string UserId, string Reason)
    : ICommand, IProjectScoped;

public sealed record CancelProjectTaskCommand(Guid ProjectId, Guid TaskId, string UserId) : ICommand, IProjectScoped;

public sealed record ReopenProjectTaskCommand(Guid ProjectId, Guid TaskId, string UserId) : ICommand, IProjectScoped;

public sealed record GetProjectTaskQuery(Guid ProjectId, Guid TaskId, string UserId)
    : IQuery<ProjectTaskResponse>, IProjectScoped;

/// <summary>E3 adds the optional filters (all ANDed): the stage, the assignee, the status.</summary>
public sealed record GetProjectTasksByProjectQuery(
    Guid ProjectId, string UserId, Guid? StageId = null, Guid? AssigneeId = null, ProjectTaskStatus? Status = null)
    : IQuery<IReadOnlyList<ProjectTaskResponse>>, IProjectScoped;

/// <summary>E3 (D2): a task added by hand. A Stage-workflow project needs <see cref="ProjectStageId"/> (and a
/// settled stage structure); a FreeStyle one must not have it. <see cref="Order"/> defaults to the end of
/// the stage.</summary>
public sealed record CreateProjectTaskCommand(
    Guid ProjectId, string UserId, Guid? ProjectStageId, string Name, string? Description, string? Notes,
    Guid? AssignedToId, TaskPriority Priority, decimal Weight, int? Order, DateTime? DueDate)
    : ICommand<Guid>, IProjectScoped;

public sealed record UpdateProjectTaskCommand(
    Guid ProjectId, Guid TaskId, string UserId, string Name, string? Description, string? Notes,
    TaskPriority Priority, decimal Weight, int Order, DateTime? DueDate)
    : ICommand, IProjectScoped;

public sealed record DeleteProjectTaskCommand(Guid ProjectId, Guid TaskId, string UserId) : ICommand, IProjectScoped;

/// <summary>E3: the task page's one-shot read — a fixed number of statements.</summary>
public sealed record ProjectTaskDetailsResponse(
    ProjectTaskResponse Task, IReadOnlyList<ProjectSubTaskResponse> SubTasks, int SubTasksCount, int CommentsCount,
    GateSummary Gates);

public sealed record GetProjectTaskDetailsQuery(Guid ProjectId, Guid TaskId, string UserId)
    : IQuery<ProjectTaskDetailsResponse>, IProjectScoped;

public sealed record ProjectSubTaskResponse(
    Guid Id, Guid ProjectTaskId, string Title, string? Description, int Order, bool IsRequired, bool IsDone);

public sealed record CompleteProjectSubTaskCommand(Guid ProjectId, Guid SubTaskId, string UserId)
    : ICommand, IProjectScoped;

/// <summary>E5. <see cref="Order"/> defaults to the end of the task's sub-tasks.</summary>
public sealed record CreateProjectSubTaskCommand(
    Guid ProjectId, Guid TaskId, string UserId, string Title, string? Description, int? Order, bool IsRequired)
    : ICommand<Guid>, IProjectScoped;

public sealed record UpdateProjectSubTaskCommand(
    Guid ProjectId, Guid TaskId, Guid SubTaskId, string UserId, string Title, string? Description, int Order, bool IsRequired)
    : ICommand, IProjectScoped;

public sealed record DeleteProjectSubTaskCommand(Guid ProjectId, Guid TaskId, Guid SubTaskId, string UserId)
    : ICommand, IProjectScoped;

/// <summary>E5: the task's assignee or a holder of <c>stage_tasks.manage</c>.</summary>
public sealed record ReopenProjectSubTaskCommand(Guid ProjectId, Guid TaskId, Guid SubTaskId, string UserId)
    : ICommand, IProjectScoped;

public sealed record GetProjectSubTasksByTaskQuery(Guid ProjectId, Guid TaskId, string UserId)
    : IQuery<IReadOnlyList<ProjectSubTaskResponse>>, IProjectScoped;

public sealed record TaskCommentResponse(Guid Id, Guid TaskId, Guid EmployeeId, string Content, DateTime CreatedAtUtc);

public sealed record AddTaskCommentCommand(Guid ProjectId, Guid TaskId, string UserId, string Content)
    : ICommand<Guid>, IProjectScoped;

public sealed record GetTaskCommentsQuery(Guid ProjectId, Guid TaskId, string UserId)
    : IQuery<IReadOnlyList<TaskCommentResponse>>, IProjectScoped;
