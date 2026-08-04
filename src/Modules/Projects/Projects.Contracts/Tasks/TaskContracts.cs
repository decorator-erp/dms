using BuildingBlocks.Messaging;

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

public sealed record GetProjectTasksByProjectQuery(Guid ProjectId, string UserId)
    : IQuery<IReadOnlyList<ProjectTaskResponse>>, IProjectScoped;

public sealed record ProjectSubTaskResponse(
    Guid Id, Guid ProjectTaskId, string Title, string? Description, int Order, bool IsRequired, bool IsDone);

public sealed record CompleteProjectSubTaskCommand(Guid ProjectId, Guid SubTaskId, string UserId)
    : ICommand, IProjectScoped;

public sealed record GetProjectSubTasksByTaskQuery(Guid ProjectId, Guid TaskId, string UserId)
    : IQuery<IReadOnlyList<ProjectSubTaskResponse>>, IProjectScoped;

public sealed record TaskCommentResponse(Guid Id, Guid TaskId, Guid EmployeeId, string Content, DateTime CreatedAtUtc);

public sealed record AddTaskCommentCommand(Guid ProjectId, Guid TaskId, string UserId, string Content)
    : ICommand<Guid>, IProjectScoped;

public sealed record GetTaskCommentsQuery(Guid ProjectId, Guid TaskId, string UserId)
    : IQuery<IReadOnlyList<TaskCommentResponse>>, IProjectScoped;
