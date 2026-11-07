using Projects.Domain;
using SharedKernel;
using Xunit;

namespace Projects.UnitTests;

public class ProjectStageTests
{
    private static ProjectStage CreateStage() => ProjectStage.Create("Design", 1, Guid.NewGuid(), totalTasks: 3);

    [Fact]
    public void Create_StartsActive()
    {
        ProjectStage stage = CreateStage();

        Assert.Equal(StageStatus.Active, stage.Status);
        Assert.Equal(3, stage.TotalTasks);
    }

    [Fact]
    public void Complete_FromActive_Succeeds_AndRaisesDomainEvent()
    {
        ProjectStage stage = CreateStage();
        var completedById = Guid.NewGuid();

        Result result = stage.Complete(completedById);

        Assert.True(result.IsSuccess);
        Assert.Equal(StageStatus.Completed, stage.Status);
        Assert.Equal(completedById, stage.CompletedById);
        IDomainEvent raised = Assert.Single(stage.DomainEvents);
        Assert.IsType<ProjectStageCompletedDomainEvent>(raised);
    }

    [Fact]
    public void Complete_Twice_SecondCallIsRejected()
    {
        ProjectStage stage = CreateStage();
        stage.Complete(Guid.NewGuid());

        Result result = stage.Complete(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(StageErrors.InvalidStatusTransition, result.Error);
    }

    [Fact]
    public void Reject_FromActive_Succeeds_AndRaisesDomainEvent()
    {
        ProjectStage stage = CreateStage();

        Result result = stage.Reject("missing drawings");

        Assert.True(result.IsSuccess);
        Assert.Equal(StageStatus.Rejected, stage.Status);
        Assert.IsType<ProjectStageRejectedDomainEvent>(Assert.Single(stage.DomainEvents));
    }

    [Fact]
    public void Complete_ALockedStage_IsRejected()
    {
        ProjectStage stage = CreateStage();
        stage.Lock();

        Result result = stage.Complete(Guid.NewGuid());

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Lock_ThenUnlock_RestoresActive()
    {
        ProjectStage stage = CreateStage();
        stage.Lock();
        Assert.Equal(StageStatus.Locked, stage.Status);

        Result result = stage.Unlock();

        Assert.True(result.IsSuccess);
        Assert.Equal(StageStatus.Active, stage.Status);
    }

    [Fact]
    public void Unlock_WhenNotLocked_IsNoOpSuccess()
    {
        ProjectStage stage = CreateStage();

        Result result = stage.Unlock();

        Assert.True(result.IsSuccess);
        Assert.Equal(StageStatus.Active, stage.Status);
    }

    [Fact]
    public void SetCost_Negative_IsRejected()
    {
        ProjectStage stage = CreateStage();

        Result result = stage.SetCost(-1m);

        Assert.True(result.IsFailure);
        Assert.Equal(StageErrors.NegativeCost, result.Error);
    }
}
