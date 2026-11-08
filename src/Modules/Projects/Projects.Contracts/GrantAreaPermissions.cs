namespace Projects.Contracts;

/// <summary>
/// P1-A: the single table of route permission codes a project access grant implies, by area and level. Levels are
/// cumulative: an Editor holds the Viewer codes too, a Manager the Editor ones. The codes are the real route codes
/// of each module (as literals: <c>Projects.Contracts</c> references no other module's contracts); a test checks
/// each is in the permission catalog.
///
/// <para><b>What may be here:</b> only codes whose every route resolves to one project and is checked by an area-aware
/// scope check: <c>IProjectScoped</c>, <c>ICrmProjectScoped</c>, <c>ISiteScoped</c>, <c>IDocumentsScoped</c>, or a
/// short allow-list of handlers that call the guard themselves (the project and site lists, site creation, the
/// grant routes, My gathering work), or a reviewed list of global read-only reference reads with no project content
/// (the site-survey form definitions <c>sites.read</c> also opens). <c>GrantAreaPermissionCoverageTests</c> walks the
/// real host's endpoints and fails otherwise.</para>
///
/// <para><b>What never is:</b> <c>project.read_all</c>, <c>project.manage_members</c>,
/// <c>project.manage_access</c>, <c>project.create</c>/<c>delete</c>/<c>update</c>/<c>change_status</c>/
/// <c>set_active</c>, <c>stage_teams.manage</c>, roles, permissions, catalogs, and every code that also guards a
/// global route — <c>stages.read</c>/<c>stages.manage</c> (stage templates), <c>file_templates.*</c>, CRM clients,
/// leads, contacts, catalog and space types. So a grant never edits the project itself, its members, teams or
/// grants (Team and Overview stay read-only at every level), and grantees reach the stage list through the project
/// details page (Overview) rather than <c>GET …/stages</c>.</para>
/// </summary>
public static class GrantAreaPermissions
{
    /// <summary>Area → level → the codes that level adds.</summary>
    public static readonly IReadOnlyDictionary<ProjectAccessArea, IReadOnlyDictionary<ProjectMemberAccess, IReadOnlyList<string>>> Table =
        new Dictionary<ProjectAccessArea, IReadOnlyDictionary<ProjectMemberAccess, IReadOnlyList<string>>>
        {
            [ProjectAccessArea.Overview] = Levels(
                viewer: [ProjectsPermissions.Projects.Read, ProjectsPermissions.Scope.Read],
                editor: [],
                manager: []),
            [ProjectAccessArea.StagesAndTasks] = Levels(
                viewer: [ProjectsPermissions.Projects.Read, ProjectsPermissions.StageTasks.Read],
                editor: [ProjectsPermissions.StageTasks.Complete, ProjectsPermissions.StageTasks.Manage],
                manager:
                [
                    ProjectsPermissions.StageTasks.Assign, ProjectsPermissions.Stages.Complete,
                    ProjectsPermissions.Stages.Reject, ProjectsPermissions.Stages.ManageGates,
                    ProjectsPermissions.StageTasks.ManageGates,
                ]),
            [ProjectAccessArea.Documents] = Levels(
                viewer: [ProjectsPermissions.Projects.Read, "documents.read"],
                editor: ["documents.upload", "documents.update"],
                manager: ["documents.delete"]),
            [ProjectAccessArea.Sites] = Levels(
                viewer: [ProjectsPermissions.Projects.Read, "sites.read"],
                editor: ["sites.create", "sites.update", "sites.submit"],
                manager: ["sites.approve", "sites.delete"]),
            [ProjectAccessArea.Crm] = Levels(
                viewer:
                [
                    ProjectsPermissions.Projects.Read, "crm.briefs.read", "crm.spaces.read", "crm.items.read",
                    "crm.artifacts.read", "crm.periods.read",
                ],
                editor: ["crm.briefs.manage", "crm.spaces.manage", "crm.items.create", "crm.items.update", "crm.artifacts.manage"],
                manager:
                [
                    "crm.briefs.send", "crm.items.send", "crm.items.supersede", "crm.periods.open", "crm.periods.extend",
                    "crm.periods.close",
                ]),
            [ProjectAccessArea.Team] = Levels(
                viewer: [ProjectsPermissions.Projects.Read],
                editor: [],
                manager: []),
        };

    /// <summary>Every code the table can imply.</summary>
    public static IReadOnlySet<string> AllCodes { get; } =
        Table.Values.SelectMany(l => l.Values).SelectMany(c => c).ToHashSet(StringComparer.Ordinal);

    /// <summary>The codes a grant of <paramref name="level"/> on <paramref name="areas"/> implies.</summary>
    public static IReadOnlySet<string> Implied(ProjectAccessArea areas, ProjectMemberAccess level)
    {
        HashSet<string> codes = new(StringComparer.Ordinal);
        foreach ((ProjectAccessArea area, IReadOnlyDictionary<ProjectMemberAccess, IReadOnlyList<string>> byLevel) in Table)
        {
            if ((areas & area) != area)
            {
                continue;
            }

            foreach ((ProjectMemberAccess required, IReadOnlyList<string> added) in byLevel)
            {
                if (level >= required)
                {
                    codes.UnionWith(added);
                }
            }
        }

        return codes;
    }

    private static Dictionary<ProjectMemberAccess, IReadOnlyList<string>> Levels(
        string[] viewer, string[] editor, string[] manager) =>
        new Dictionary<ProjectMemberAccess, IReadOnlyList<string>>
        {
            [ProjectMemberAccess.Viewer] = viewer,
            [ProjectMemberAccess.Editor] = editor,
            [ProjectMemberAccess.Manager] = manager,
        };
}
