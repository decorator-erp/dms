using Projects.Domain;
using SharedKernel;
using Xunit;

namespace Projects.UnitTests;

/// <summary>P1-A: the one active-window rule, on a pinned clock — <see cref="ProjectAccessGrant.IsActiveAt"/>, its SQL
/// twin <see cref="ProjectAccessGrant.ActiveAt"/> (compiled here), and <see cref="ProjectAccessGrant.StatusAt"/>
/// agree at every boundary.</summary>
public class ProjectAccessGrantTests
{
    private static readonly DateTime Start = new(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = Start.AddDays(1);

    private static ProjectAccessGrant Grant(DateTime? expiresAt) => ProjectAccessGrant.Create(
        Guid.NewGuid(), Guid.NewGuid(), ProjectMemberAccess.Viewer, ProjectAccessArea.Documents, [],
        Start, expiresAt, "  Q3 audit  ", Guid.NewGuid(), Start.AddHours(-1));

    public static IEnumerable<object[]> Window() =>
    [
        [Start.AddTicks(-1), false, (int)ProjectAccessGrantStatus.Upcoming],
        [Start, true, (int)ProjectAccessGrantStatus.Active],
        [End.AddTicks(-1), true, (int)ProjectAccessGrantStatus.Active],
        [End, false, (int)ProjectAccessGrantStatus.Expired],
        [End.AddYears(1), false, (int)ProjectAccessGrantStatus.Expired],
    ];

    [Theory]
    [MemberData(nameof(Window))]
    public void Active_from_the_start_inclusive_until_the_end_exclusive(DateTime now, bool active, int status)
    {
        ProjectAccessGrant grant = Grant(End);

        Assert.Equal(active, grant.IsActiveAt(now));
        Assert.Equal(active, ProjectAccessGrant.ActiveAt(now).Compile()(grant));
        Assert.Equal((ProjectAccessGrantStatus)status, grant.StatusAt(now));
    }

    [Fact]
    public void No_end_is_active_forever_once_started()
    {
        ProjectAccessGrant grant = Grant(expiresAt: null);

        Assert.False(grant.IsActiveAt(Start.AddTicks(-1)));
        Assert.True(grant.IsActiveAt(Start.AddYears(50)));
        Assert.True(ProjectAccessGrant.CurrentOrUpcomingAt(Start.AddYears(50)).Compile()(grant));
        Assert.Equal("Q3 audit", grant.Reason);
    }

    [Fact]
    public void Revoke_is_final_and_immediate()
    {
        ProjectAccessGrant grant = Grant(End);
        DateTime revokedAt = Start.AddHours(2);

        Assert.True(grant.Revoke(Guid.NewGuid(), " done ", revokedAt).IsSuccess);

        Assert.False(grant.IsActiveAt(revokedAt));
        Assert.False(ProjectAccessGrant.ActiveAt(Start.AddHours(1)).Compile()(grant));
        Assert.False(ProjectAccessGrant.CurrentOrUpcomingAt(revokedAt).Compile()(grant));
        Assert.Equal(ProjectAccessGrantStatus.Revoked, grant.StatusAt(revokedAt));
        Assert.Equal("done", grant.RevokeReason);
        Assert.Equal(ProjectAccessGrantErrors.AlreadyRevoked, grant.Revoke(Guid.NewGuid(), null, revokedAt).Error);
        Assert.Equal(ProjectAccessGrantErrors.NotEditable, grant.Update(
            ProjectMemberAccess.Editor, ProjectAccessArea.Documents, [], null, "x", revokedAt).Error);
    }

    [Fact]
    public void An_expired_grant_cannot_be_edited_or_revoked()
    {
        ProjectAccessGrant grant = Grant(End);

        Result update = grant.Update(ProjectMemberAccess.Manager, ProjectAccessArea.Sites, [], End.AddDays(1), "later", End);
        Result revoke = grant.Revoke(Guid.NewGuid(), null, End);

        Assert.Equal(ProjectAccessGrantErrors.NotEditable, update.Error);
        Assert.Equal(ProjectAccessGrantErrors.NotEditable, revoke.Error);
        Assert.Equal(ProjectMemberAccess.Viewer, grant.Level);
    }

    [Fact]
    public void An_upcoming_or_active_grant_is_edited_in_place()
    {
        ProjectAccessGrant grant = Grant(End);
        var stage = Guid.NewGuid();

        Result update = grant.Update(
            ProjectMemberAccess.Editor, ProjectAccessArea.Documents | ProjectAccessArea.StagesAndTasks, [stage, stage],
            End.AddDays(6), "extended", Start.AddHours(1));

        Assert.True(update.IsSuccess);
        Assert.Equal(ProjectMemberAccess.Editor, grant.Level);
        Assert.Equal([stage], grant.StageIds);
        Assert.True(grant.IsActiveAt(End.AddDays(5)));
    }
}
