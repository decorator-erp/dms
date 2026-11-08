using Projects.Contracts;
using Xunit;

namespace Projects.Application.Tests;

/// <summary>P1-A: the shape of <see cref="GrantAreaPermissions"/> — cumulative levels, areas combine, and the codes
/// a grant must never imply are absent. Which routes the codes open is
/// <c>Integration.Tests.GrantAreaPermissionCoverageTests</c>' job.</summary>
public class GrantAreaPermissionsTests
{
    private static readonly string[] NeverImplied =
    [
        ProjectsPermissions.Projects.ReadAll, ProjectsPermissions.Projects.ManageMembers,
        ProjectsPermissions.Projects.ManageAccess, ProjectsPermissions.Projects.Create, ProjectsPermissions.Projects.Delete,
        ProjectsPermissions.Projects.Update, ProjectsPermissions.Projects.ChangeStatus, ProjectsPermissions.Projects.SetActive,
        ProjectsPermissions.Stages.Read, ProjectsPermissions.Stages.Manage, ProjectsPermissions.StageTeams.Manage,
        ProjectsPermissions.Scope.Manage, ProjectsPermissions.Scope.Confirm,
        "file_templates.read", "file_templates.manage", "clients.read", "crm.clients.read_all", "crm.catalog.manage",
        "crm.space_types.manage", "sites.definitions.manage", "roles.read", "permissions.manage",
    ];

    [Fact]
    public void Every_area_has_every_level()
    {
        ProjectAccessArea[] areas = [.. Enum.GetValues<ProjectAccessArea>().Where(a => a != ProjectAccessArea.None)];

        Assert.Equal(areas.Order(), GrantAreaPermissions.Table.Keys.Order());
        Assert.All(GrantAreaPermissions.Table.Values, byLevel =>
            Assert.Equal(Enum.GetValues<ProjectMemberAccess>().Order(), byLevel.Keys.Order()));
    }

    [Fact]
    public void Levels_are_cumulative()
    {
        foreach (ProjectAccessArea area in GrantAreaPermissions.Table.Keys)
        {
            IReadOnlySet<string> viewer = GrantAreaPermissions.Implied(area, ProjectMemberAccess.Viewer);
            IReadOnlySet<string> editor = GrantAreaPermissions.Implied(area, ProjectMemberAccess.Editor);
            IReadOnlySet<string> manager = GrantAreaPermissions.Implied(area, ProjectMemberAccess.Manager);

            Assert.Contains(ProjectsPermissions.Projects.Read, viewer);
            Assert.True(viewer.IsSubsetOf(editor), area.ToString());
            Assert.True(editor.IsSubsetOf(manager), area.ToString());
        }
    }

    [Fact]
    public void Areas_combine_and_none_implies_nothing()
    {
        IReadOnlySet<string> both = GrantAreaPermissions.Implied(
            ProjectAccessArea.Documents | ProjectAccessArea.Sites, ProjectMemberAccess.Viewer);

        Assert.Equal(["documents.read", ProjectsPermissions.Projects.Read, "sites.read"], both.Order());
        Assert.Empty(GrantAreaPermissions.Implied(ProjectAccessArea.None, ProjectMemberAccess.Manager));
    }

    [Fact]
    public void Viewer_reads_and_writes_need_editor()
    {
        Assert.DoesNotContain("documents.upload", GrantAreaPermissions.Implied(ProjectAccessArea.Documents, ProjectMemberAccess.Viewer));
        Assert.Contains("documents.upload", GrantAreaPermissions.Implied(ProjectAccessArea.Documents, ProjectMemberAccess.Editor));
        Assert.DoesNotContain("documents.delete", GrantAreaPermissions.Implied(ProjectAccessArea.Documents, ProjectMemberAccess.Editor));
        Assert.Contains("documents.delete", GrantAreaPermissions.Implied(ProjectAccessArea.Documents, ProjectMemberAccess.Manager));
    }

    [Fact]
    public void Overview_and_team_stay_read_only_at_every_level()
    {
        Assert.Equal(
            GrantAreaPermissions.Implied(ProjectAccessArea.Overview, ProjectMemberAccess.Viewer),
            GrantAreaPermissions.Implied(ProjectAccessArea.Overview, ProjectMemberAccess.Manager));
        Assert.Equal(
            [ProjectsPermissions.Projects.Read],
            GrantAreaPermissions.Implied(ProjectAccessArea.Team, ProjectMemberAccess.Manager));
    }

    [Fact]
    public void Never_implies_a_global_or_project_management_code() =>
        Assert.Empty(GrantAreaPermissions.AllCodes.Intersect(NeverImplied));
}
