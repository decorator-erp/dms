using SharedKernel;

namespace Projects.Domain;

/// <summary>Ported from <c>../backend/Domain/Entities/ProjectTask.cs</c>. <see cref="AssignedToId"/>/
/// <see cref="DepartmentId"/> are bare Guids → <c>hr.employees</c>/<c>hr.departments</c>, never
/// navigations. <see cref="Create"/> enforces the backend's <c>WorkflowType</c> rule: a
/// <see cref="WorkflowType.Stage"/> project's tasks must belong to a stage; a
/// <see cref="WorkflowType.FreeStyle"/> project's must not.</summary>
internal sealed class ProjectTask : Entity, IAuditable
{
    public Guid ProjectId { get; private set; }
    public Guid? ProjectStageId { get; private set; }
    public Guid? StageTaskId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public int Order { get; private set; }
    public ProjectTaskStatus Status { get; private set; }
    public TaskPriority Priority { get; private set; }
    public decimal Weight { get; private set; }
    public string? Notes { get; private set; }
    public DateTime? DueDate { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public Guid? AssignedToId { get; private set; }
    public Guid? DepartmentId { get; private set; }
    public decimal? Cost { get; private set; }
    /// <summary>P14: <see cref="TotalSubTasks"/>/<see cref="CompletedSubTasks"/> count REQUIRED sub-tasks only, so
    /// an optional sub-task left open never blocks <see cref="Complete"/>.</summary>
    public int TotalSubTasks { get; private set; }
    public int CompletedSubTasks { get; private set; }

    private ProjectTask()
    {
        // EF materialization.
    }

    public static Result<ProjectTask> Create(
        Guid projectId, string name, int order, WorkflowType workflowType,
        decimal weight = 1.00m, Guid? projectStageId = null, Guid? stageTaskId = null,
        TaskPriority priority = TaskPriority.Medium, ProjectTaskStatus status = ProjectTaskStatus.Open,
        string? description = null, int totalSubTasks = 0, Guid? departmentId = null, DateTime? dueDate = null)
    {
        if (workflowType == WorkflowType.Stage && projectStageId is null)
        {
            return TaskErrors.StageRequired;
        }

        if (workflowType == WorkflowType.FreeStyle && projectStageId is not null)
        {
            return TaskErrors.StageNotAllowed;
        }

        return new ProjectTask
        {
            ProjectId = projectId,
            Name = name,
            Order = order,
            Weight = weight,
            ProjectStageId = projectStageId,
            StageTaskId = stageTaskId,
            Priority = priority,
            Status = status,
            Description = description,
            TotalSubTasks = totalSubTasks,
            DepartmentId = departmentId,
            DueDate = dueDate,
        };
    }

    /// <summary>E3 (D2): the office edits a task by hand. Status, assignment and progress are untouched.</summary>
    public void Update(
        string name, string? description, string? notes, TaskPriority priority, decimal weight, int order, DateTime? dueDate)
    {
        Name = name;
        Description = description;
        Notes = notes;
        Priority = priority;
        Weight = weight;
        Order = order;
        DueDate = dueDate;
    }

    public Result SetCost(decimal? cost)
    {
        if (cost is < 0)
        {
            return TaskErrors.NegativeCost;
        }

        Cost = cost;
        return Result.Success();
    }

    public Result Assign(Guid employeeId)
    {
        if (Status is ProjectTaskStatus.Completed or ProjectTaskStatus.Cancelled)
        {
            return TaskErrors.InvalidStatusTransition;
        }

        AssignedToId = employeeId;
        AddDomainEvent(new ProjectTaskAssignedDomainEvent(Id, ProjectId, Name, employeeId));
        return Result.Success();
    }

    public Result Start()
    {
        if (Status != ProjectTaskStatus.Open)
        {
            return TaskErrors.InvalidStatusTransition;
        }

        Status = ProjectTaskStatus.InProgress;
        StartedAtUtc = DateTime.UtcNow;
        AddDomainEvent(new ProjectTaskStartedDomainEvent(Id, ProjectId, Name, AssignedToId));
        return Result.Success();
    }

    public Result Complete()
    {
        if (Status != ProjectTaskStatus.InProgress)
        {
            return TaskErrors.InvalidStatusTransition;
        }

        if (CompletedSubTasks < TotalSubTasks)
        {
            return TaskErrors.SubTasksIncomplete;
        }

        Status = ProjectTaskStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
        AddDomainEvent(new ProjectTaskCompletedDomainEvent(Id, ProjectId, Name, AssignedToId));
        return Result.Success();
    }

    public Result Reject(string reason)
    {
        if (Status is not (ProjectTaskStatus.InProgress or ProjectTaskStatus.PendingApproval))
        {
            return TaskErrors.InvalidStatusTransition;
        }

        Status = ProjectTaskStatus.Rejected;
        Notes = reason;
        AddDomainEvent(new ProjectTaskRejectedDomainEvent(Id, ProjectId, Name, AssignedToId));
        return Result.Success();
    }

    public Result Cancel()
    {
        if (Status is ProjectTaskStatus.Completed or ProjectTaskStatus.Cancelled)
        {
            return TaskErrors.InvalidStatusTransition;
        }

        Status = ProjectTaskStatus.Cancelled;
        AddDomainEvent(new ProjectTaskCancelledDomainEvent(Id, ProjectId, Name, AssignedToId));
        return Result.Success();
    }

    public Result Reopen()
    {
        if (Status is not (ProjectTaskStatus.Completed or ProjectTaskStatus.Rejected or ProjectTaskStatus.Cancelled))
        {
            return TaskErrors.InvalidStatusTransition;
        }

        Status = ProjectTaskStatus.Open;
        CompletedAtUtc = null;
        AddDomainEvent(new ProjectTaskReopenedDomainEvent(Id, ProjectId, Name, AssignedToId));
        return Result.Success();
    }

    /// <summary>NP2: an <c>Unlock</c>-trigger gate may lock this task only while it is Open or already Locked.</summary>
    public bool IsLockable => Status is ProjectTaskStatus.Open or ProjectTaskStatus.Locked;

    /// <summary>Called when the owning stage locks (cascades — a stage-workflow task can't start work
    /// while its stage is locked) or when an <c>Unlock</c>-trigger gate is created directly against this
    /// task.</summary>
    public void Lock() => Status = ProjectTaskStatus.Locked;

    public Result Unlock()
    {
        if (Status != ProjectTaskStatus.Locked)
        {
            return Result.Success();
        }

        Status = ProjectTaskStatus.Open;
        return Result.Success();
    }

    public void RecalculateSubTaskProgress(int completedSubTasks, int totalSubTasks)
    {
        CompletedSubTasks = completedSubTasks;
        TotalSubTasks = totalSubTasks;
    }
}
