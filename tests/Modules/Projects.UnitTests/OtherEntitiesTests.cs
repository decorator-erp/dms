using Projects.Domain;
using SharedKernel;
using Xunit;

namespace Projects.UnitTests;

public class ProjectSubTaskTests
{
    [Fact]
    public void Complete_Succeeds_ThenSecondCompleteIsRejected()
    {
        var subTask = ProjectSubTask.Create(Guid.NewGuid(), "Attach rebar", 1);
        var completedById = Guid.NewGuid();

        Result first = subTask.Complete(completedById);
        Assert.True(first.IsSuccess);
        Assert.True(subTask.IsDone);
        Assert.Equal(completedById, subTask.CompletedById);

        Result second = subTask.Complete(Guid.NewGuid());
        Assert.True(second.IsFailure);
        Assert.Equal(SubTaskErrors.AlreadyDone, second.Error);
    }
}

public class TaskCommentTests
{
    [Fact]
    public void Create_ThenUpdateContent_ChangesContent()
    {
        var comment = TaskComment.Create("initial", Guid.NewGuid(), Guid.NewGuid());

        comment.UpdateContent("revised");

        Assert.Equal("revised", comment.Content);
    }
}

public class StageTeamTests
{
    [Fact]
    public void CreateProjectDepartmentTeam_WithEmptyHead_Throws() => Assert.Throws<ArgumentException>(() =>
                                                                               StageTeam.CreateProjectDepartmentTeam(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty));

    [Fact]
    public void AddMember_Twice_ForSameEmployee_IsRejected()
    {
        var team = StageTeam.CreateProjectDepartmentTeam(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var employeeId = Guid.NewGuid();

        Result<StageTeamMember> first = team.AddMember(employeeId, StageTeamMemberRole.Responsible);
        Assert.True(first.IsSuccess);

        Result<StageTeamMember> second = team.AddMember(employeeId, StageTeamMemberRole.Observer);
        Assert.True(second.IsFailure);
        Assert.Equal(TeamErrors.MemberAlreadyExists, second.Error);
        Assert.Single(team.Members);
    }

    [Fact]
    public void ChangeHead_WithEmptyGuid_Throws()
    {
        var team = StageTeam.CreateProjectStandaloneTeam(Guid.NewGuid(), Guid.NewGuid());

        Assert.Throws<ArgumentException>(() => team.ChangeHead(Guid.Empty));
    }
}

public class StageTeamMemberTests
{
    // See ProjectTests.ChangeStatus_EnumeratesEveryTransition's comment: xunit test methods must be
    // public, StageTeamMemberRole is internal - theory data travels as int.
    [Theory]
    [InlineData((int)StageTeamMemberRole.Responsible, true)]
    [InlineData((int)StageTeamMemberRole.Contributor, true)]
    [InlineData((int)StageTeamMemberRole.Reviewer, false)]
    [InlineData((int)StageTeamMemberRole.Observer, false)]
    public void CanExecuteTasks_ReflectsRole(int roleValue, bool canExecute)
    {
        var member = StageTeamMember.Create(Guid.NewGuid(), Guid.NewGuid(), (StageTeamMemberRole)roleValue);

        Assert.Equal(canExecute, member.CanExecuteTasks);
    }

    [Fact]
    public void ChangeRole_UpdatesCanExecuteTasks()
    {
        var member = StageTeamMember.Create(Guid.NewGuid(), Guid.NewGuid(), StageTeamMemberRole.Observer);

        member.ChangeRole(StageTeamMemberRole.Responsible);

        Assert.True(member.CanExecuteTasks);
    }
}
