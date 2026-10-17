using Projects.Domain;
using SharedKernel;
using Xunit;

namespace Projects.UnitTests;

public class ProjectTaskTests
{
    private static ProjectTask CreateOpenTask() =>
        ProjectTask.Create(Guid.NewGuid(), "Pour foundation", 1, WorkflowType.Stage, projectStageId: Guid.NewGuid()).Value!;

    [Fact]
    public void Create_StageWorkflow_WithoutStageId_IsRejected()
    {
        Result<ProjectTask> result = ProjectTask.Create(Guid.NewGuid(), "Task", 1, WorkflowType.Stage, projectStageId: null);

        Assert.True(result.IsFailure);
        Assert.Equal(TaskErrors.StageRequired, result.Error);
    }

    [Fact]
    public void Create_FreeStyleWorkflow_WithStageId_IsRejected()
    {
        Result<ProjectTask> result = ProjectTask.Create(Guid.NewGuid(), "Task", 1, WorkflowType.FreeStyle, projectStageId: Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(TaskErrors.StageNotAllowed, result.Error);
    }

    [Fact]
    public void Create_FreeStyleWorkflow_WithoutStageId_Succeeds()
    {
        Result<ProjectTask> result = ProjectTask.Create(Guid.NewGuid(), "Task", 1, WorkflowType.FreeStyle);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProjectTaskStatus.Open, result.Value!.Status);
    }

    [Fact]
    public void FullLifecycle_Assign_Start_Complete_RaisesAllThreeDomainEvents()
    {
        ProjectTask task = CreateOpenTask();
        var employeeId = Guid.NewGuid();

        Assert.True(task.Assign(employeeId).IsSuccess);
        Assert.Equal(employeeId, task.AssignedToId);

        Assert.True(task.Start().IsSuccess);
        Assert.Equal(ProjectTaskStatus.InProgress, task.Status);
        Assert.NotNull(task.StartedAtUtc);

        Assert.True(task.Complete().IsSuccess);
        Assert.Equal(ProjectTaskStatus.Completed, task.Status);
        Assert.NotNull(task.CompletedAtUtc);

        Assert.Equal(3, task.DomainEvents.Count);
        Assert.Collection(task.DomainEvents,
            e => Assert.IsType<ProjectTaskAssignedDomainEvent>(e),
            e => Assert.IsType<ProjectTaskStartedDomainEvent>(e),
            e => Assert.IsType<ProjectTaskCompletedDomainEvent>(e));
    }

    [Fact]
    public void Start_WithoutOpenStatus_IsRejected()
    {
        ProjectTask task = CreateOpenTask();
        task.Start();

        Result result = task.Start();

        Assert.True(result.IsFailure);
        Assert.Equal(TaskErrors.InvalidStatusTransition, result.Error);
    }

    [Fact]
    public void Complete_WithIncompleteRequiredSubTasks_IsRejected()
    {
        ProjectTask task = CreateOpenTask();
        task.Start();
        task.RecalculateSubTaskProgress(completedSubTasks: 1, totalSubTasks: 2);

        Result result = task.Complete();

        Assert.True(result.IsFailure);
        Assert.Equal(TaskErrors.SubTasksIncomplete, result.Error);
    }

    [Fact]
    public void Reject_FromInProgress_Succeeds()
    {
        ProjectTask task = CreateOpenTask();
        task.Start();

        Result result = task.Reject("bad workmanship");

        Assert.True(result.IsSuccess);
        Assert.Equal(ProjectTaskStatus.Rejected, task.Status);
    }

    [Fact]
    public void Cancel_FromOpen_Succeeds()
    {
        ProjectTask task = CreateOpenTask();

        Result result = task.Cancel();

        Assert.True(result.IsSuccess);
        Assert.Equal(ProjectTaskStatus.Cancelled, task.Status);
    }

    [Fact]
    public void Cancel_AnAlreadyCompletedTask_IsRejected()
    {
        ProjectTask task = CreateOpenTask();
        task.Start();
        task.Complete();

        Result result = task.Cancel();

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Reopen_ACancelledTask_ReturnsToOpen()
    {
        ProjectTask task = CreateOpenTask();
        task.Cancel();

        Result result = task.Reopen();

        Assert.True(result.IsSuccess);
        Assert.Equal(ProjectTaskStatus.Open, task.Status);
    }

    [Fact]
    public void Reopen_AnOpenTask_IsRejected()
    {
        ProjectTask task = CreateOpenTask();

        Result result = task.Reopen();

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Lock_ThenUnlock_RestoresOpen()
    {
        ProjectTask task = CreateOpenTask();
        task.Lock();
        Assert.Equal(ProjectTaskStatus.Locked, task.Status);

        Result result = task.Unlock();

        Assert.True(result.IsSuccess);
        Assert.Equal(ProjectTaskStatus.Open, task.Status);
    }
}
