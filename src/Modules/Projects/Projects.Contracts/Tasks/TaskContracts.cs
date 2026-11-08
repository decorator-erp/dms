using BuildingBlocks.Messaging;
using Projects.Contracts.Projects;

namespace Projects.Contracts.Tasks;

public sealed record ProjectTaskResponse(
    Guid Id, Guid ProjectId, Guid? ProjectStageId, string Name, string? Description, int Order,
    ProjectTaskStatus Status, TaskPriority Priority, decimal Weight, Guid? AssignedToId, Guid? DepartmentId,
    DateTime? DueDate, decimal? Cost, string? Notes);

/// <summary><paramref name="EmployeeId"/> is validated via <c>Hr.Contracts.IHrDirectory.GetAsync</c> in
/// the handler (G5-07) — an unknown/inactive employee id is rejected, never silently stored.</summary>
public sealed record AssignProjectTaskCommand(Guid ProjectId, Guid TaskId, string UserId, Guid EmployeeId)
    : ICommand, IProjectScoped, IRequiresProjectManager;

public sealed record StartProjectTaskCommand(Guid ProjectId, Guid TaskId, string UserId) : ICommand, IProjectScoped, IRequiresProjectEditor;

public sealed record CompleteProjectTaskCommand(Guid ProjectId, Guid TaskId, string UserId) : ICommand, IProjectScoped, IRequiresProjectEditor;

public sealed record RejectProjectTaskCommand(Guid ProjectId, Guid TaskId, string UserId, string Reason)
    : ICommand, IProjectScoped, IRequiresProjectManager;

public sealed record CancelProjectTaskCommand(Guid ProjectId, Guid TaskId, string UserId) : ICommand, IProjectScoped, IRequiresProjectManager;

public sealed record ReopenProjectTaskCommand(Guid ProjectId, Guid TaskId, string UserId) : ICommand, IProjectScoped, IRequiresProjectManager;

public sealed record GetProjectTaskQuery(Guid ProjectId, Guid TaskId, string UserId)
    : IQuery<ProjectTaskResponse>, IProjectScoped;

/// <summary>E3 adds the optional filters (all ANDed): the stage, the assignee, the status. Ordered by task order then
/// id. P13/R1-5: paging is opt-in — with neither <see cref="PageNumber"/> nor <see cref="PageSize"/> every task comes
/// back (as before P13); with either, one page, the other defaulting to 1 / 200, within 1..1,000,000 and 1..200 (the
/// project list's caps).</summary>
public sealed record GetProjectTasksByProjectQuery(
    Guid ProjectId, string UserId, Guid? StageId = null, Guid? AssigneeId = null, ProjectTaskStatus? Status = null,
    int? PageNumber = null, int? PageSize = null)
    : IQuery<IReadOnlyList<ProjectTaskResponse>>, IProjectScoped;

/// <summary>E3 (D2): a task added by hand. A Stage-workflow project needs <see cref="ProjectStageId"/> (and a
/// settled stage structure); a FreeStyle one must not have it. <see cref="Order"/> defaults to the end of
/// the stage.</summary>
public sealed record CreateProjectTaskCommand(
    Guid ProjectId, string UserId, Guid? ProjectStageId, string Name, string? Description, string? Notes,
    Guid? AssignedToId, TaskPriority Priority, decimal Weight, int? Order, DateTime? DueDate)
    : ICommand<Guid>, IProjectScoped, IRequiresProjectManager;

/// <summary>E3. A null <see cref="Priority"/>, <see cref="Weight"/> or <see cref="Order"/> keeps the current
/// value (FINDING-E-5).</summary>
public sealed record UpdateProjectTaskCommand(
    Guid ProjectId, Guid TaskId, string UserId, string Name, string? Description, string? Notes,
    TaskPriority? Priority, decimal? Weight, int? Order, DateTime? DueDate)
    : ICommand, IProjectScoped, IRequiresProjectManager;

public sealed record DeleteProjectTaskCommand(Guid ProjectId, Guid TaskId, string UserId) : ICommand, IProjectScoped, IRequiresProjectManager;

/// <summary>E3: the task page's one-shot read — a fixed number of statements.</summary>
public sealed record ProjectTaskDetailsResponse(
    ProjectTaskResponse Task, IReadOnlyList<ProjectSubTaskResponse> SubTasks, int SubTasksCount, int CommentsCount,
    GateSummary Gates);

public sealed record GetProjectTaskDetailsQuery(Guid ProjectId, Guid TaskId, string UserId)
    : IQuery<ProjectTaskDetailsResponse>, IProjectScoped;

public sealed record ProjectSubTaskResponse(
    Guid Id, Guid ProjectTaskId, string Title, string? Description, int Order, bool IsRequired, bool IsDone);

public sealed record CompleteProjectSubTaskCommand(Guid ProjectId, Guid SubTaskId, string UserId)
    : ICommand, IProjectScoped, IRequiresProjectEditor;

/// <summary>E5. <see cref="Order"/> defaults to the end of the task's sub-tasks.</summary>
public sealed record CreateProjectSubTaskCommand(
    Guid ProjectId, Guid TaskId, string UserId, string Title, string? Description, int? Order, bool IsRequired)
    : ICommand<Guid>, IProjectScoped, IRequiresProjectEditor;

/// <summary>E5. A null <see cref="Order"/> or <see cref="IsRequired"/> keeps the current value (FINDING-E-5).</summary>
public sealed record UpdateProjectSubTaskCommand(
    Guid ProjectId, Guid TaskId, Guid SubTaskId, string UserId, string Title, string? Description, int? Order, bool? IsRequired)
    : ICommand, IProjectScoped, IRequiresProjectEditor;

public sealed record DeleteProjectSubTaskCommand(Guid ProjectId, Guid TaskId, Guid SubTaskId, string UserId)
    : ICommand, IProjectScoped, IRequiresProjectEditor;

/// <summary>E5: the task's assignee or a holder of <c>stage_tasks.manage</c>.</summary>
public sealed record ReopenProjectSubTaskCommand(Guid ProjectId, Guid TaskId, Guid SubTaskId, string UserId)
    : ICommand, IProjectScoped, IRequiresProjectEditor;

public sealed record GetProjectSubTasksByTaskQuery(Guid ProjectId, Guid TaskId, string UserId)
    : IQuery<IReadOnlyList<ProjectSubTaskResponse>>, IProjectScoped;

/// <remarks>E6 adds <see cref="UpdatedAtUtc"/> (null until the comment is edited).</remarks>
public sealed record TaskCommentResponse(
    Guid Id, Guid TaskId, Guid EmployeeId, string Content, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc = null);

/// <summary>E6: the author, or a holder of <c>stage_tasks.manage</c>.</summary>
public sealed record UpdateTaskCommentCommand(Guid ProjectId, Guid TaskId, Guid CommentId, string UserId, string Content)
    : ICommand, IProjectScoped, IRequiresProjectEditor;

/// <summary>E6: the author, or a holder of <c>stage_tasks.manage</c>. A hard delete: nothing references a
/// comment.</summary>
public sealed record DeleteTaskCommentCommand(Guid ProjectId, Guid TaskId, Guid CommentId, string UserId)
    : ICommand, IProjectScoped, IRequiresProjectEditor;

public sealed record AddTaskCommentCommand(Guid ProjectId, Guid TaskId, string UserId, string Content)
    : ICommand<Guid>, IProjectScoped, IRequiresProjectEditor;

/// <summary>Oldest first then by id. P13/R1-5: paging is opt-in, as on the task list — neither parameter returns
/// every comment; either one pages, with the same defaults and caps.</summary>
public sealed record GetTaskCommentsQuery(Guid ProjectId, Guid TaskId, string UserId, int? PageNumber = null, int? PageSize = null)
    : IQuery<IReadOnlyList<TaskCommentResponse>>, IProjectScoped;
