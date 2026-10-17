using Projects.Domain;
using SharedKernel;
using Xunit;

namespace Projects.UnitTests;

public class ProjectTests
{
    private static Project CreateProject() =>
        Project.Create("P-001", "Test Project", "123 Main St", WorkflowType.Stage, Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void Create_StartsInDraft()
    {
        Project project = CreateProject();

        Assert.Equal(ProjectStatus.Draft, project.Status);
        Assert.True(project.IsActive);
        Assert.Null(project.ActualStartDate);
    }

    /// <summary>Drives a fresh project through zero or more real transitions to reach <paramref name="from"/>.</summary>
    private static Project CreateProjectAt(ProjectStatus from)
    {
        Project project = CreateProject();
        switch (from)
        {
            case ProjectStatus.Draft:
                break;
            case ProjectStatus.InProgress:
                Assert.True(project.ChangeStatus(ProjectStatus.InProgress).IsSuccess);
                break;
            case ProjectStatus.OnHold:
                Assert.True(project.ChangeStatus(ProjectStatus.InProgress).IsSuccess);
                Assert.True(project.ChangeStatus(ProjectStatus.OnHold).IsSuccess);
                break;
            case ProjectStatus.Completed:
                Assert.True(project.ChangeStatus(ProjectStatus.InProgress).IsSuccess);
                Assert.True(project.ChangeStatus(ProjectStatus.Completed).IsSuccess);
                break;
            case ProjectStatus.Cancelled:
                Assert.True(project.ChangeStatus(ProjectStatus.Cancelled).IsSuccess);
                break;
        }
        return project;
    }

    // xunit test methods must be public (xUnit1000), but ProjectStatus is internal (arch rule 4) - a
    // public method can't expose a less-accessible parameter type (CS0051), so the theory data travels
    // as int and is cast back to the enum inside the method body.
    [Theory]
    [InlineData((int)ProjectStatus.Draft, (int)ProjectStatus.InProgress, true)]
    [InlineData((int)ProjectStatus.Draft, (int)ProjectStatus.Cancelled, true)]
    [InlineData((int)ProjectStatus.Draft, (int)ProjectStatus.Completed, false)]
    [InlineData((int)ProjectStatus.Draft, (int)ProjectStatus.OnHold, false)]
    [InlineData((int)ProjectStatus.InProgress, (int)ProjectStatus.OnHold, true)]
    [InlineData((int)ProjectStatus.InProgress, (int)ProjectStatus.Completed, true)]
    [InlineData((int)ProjectStatus.InProgress, (int)ProjectStatus.Cancelled, true)]
    [InlineData((int)ProjectStatus.InProgress, (int)ProjectStatus.Draft, false)]
    [InlineData((int)ProjectStatus.OnHold, (int)ProjectStatus.InProgress, true)]
    [InlineData((int)ProjectStatus.OnHold, (int)ProjectStatus.Cancelled, true)]
    [InlineData((int)ProjectStatus.OnHold, (int)ProjectStatus.Completed, false)]
    [InlineData((int)ProjectStatus.Completed, (int)ProjectStatus.InProgress, false)]
    [InlineData((int)ProjectStatus.Cancelled, (int)ProjectStatus.InProgress, false)]
    public void ChangeStatus_EnumeratesEveryTransition(int fromValue, int toValue, bool allowed)
    {
        var from = (ProjectStatus)fromValue;
        var to = (ProjectStatus)toValue;
        Project project = CreateProjectAt(from);

        Result result = project.ChangeStatus(to);

        Assert.Equal(allowed, result.IsSuccess);
        if (allowed)
        {
            Assert.Equal(to, project.Status);
        }
        else
        {
            Assert.Equal(from, project.Status);
        }
    }

    [Fact]
    public void ChangeStatus_ToInProgress_SetsActualStartDate()
    {
        Project project = CreateProject();

        project.ChangeStatus(ProjectStatus.InProgress);

        Assert.NotNull(project.ActualStartDate);
    }

    [Fact]
    public void ChangeStatus_ToCompleted_SetsActualEndDate()
    {
        Project project = CreateProject();
        project.ChangeStatus(ProjectStatus.InProgress);

        project.ChangeStatus(ProjectStatus.Completed);

        Assert.NotNull(project.ActualEndDate);
    }

    [Fact]
    public void ChangeStatus_ToSameStatus_IsNoOpSuccess()
    {
        Project project = CreateProject();

        Result result = project.ChangeStatus(ProjectStatus.Draft);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProjectStatus.Draft, project.Status);
    }

    [Fact]
    public void Update_ChangesOnlyProvidedFields()
    {
        Project project = CreateProject();

        project.Update(name: "Renamed");

        Assert.Equal("Renamed", project.Name);
        Assert.Equal("123 Main St", project.Address);
    }
}
