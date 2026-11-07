namespace Projects.Contracts;

/// <summary>
/// Ported verbatim from <c>../backend/Domain/Constants/DefaultPermissions.cs</c>, including the
/// deliberately non-uniform naming the backend itself uses: <see cref="Stages"/> governs the runtime
/// <c>ProjectStage</c> entity AND template <c>Stage</c>/<c>StageTask</c>/<c>StageSubTask</c>/
/// <c>StageTemplate</c> CRUD AND stage gate conditions (there is no separate template-CRUD permission
/// group); <see cref="StageTasks"/> governs <c>ProjectTask</c>-level actions including task gate
/// conditions and approval request/decide. Not "cleaned up" into a more regular scheme — that would be a
/// design change this plan never asked for.
/// </summary>
public static class ProjectsPermissions
{
    public static class Projects
    {
        public const string Read = "project.read";

        /// <summary>Bypasses stage-team membership row checks entirely — see
        /// <see cref="IProjectAccessGuard"/>.</summary>
        public const string ReadAll = "project.read_all";

        public const string Create = "project.create";
        public const string Update = "project.update";
        public const string SetActive = "project.set_active";
        public const string ChangeStatus = "project.change_status";
        public const string Delete = "project.delete";

        /// <summary>E1 (D1): add, re-level and remove project members. Reading the member list needs only
        /// <see cref="Read"/>.</summary>
        public const string ManageMembers = "project.manage_members";
    }

    public static class Stages
    {
        public const string Read = "stages.read";
        public const string Manage = "stages.manage";
        public const string Complete = "stages.complete";
        public const string Approve = "stages.approve";
        public const string Reject = "stages.reject";
        public const string ManageGates = "stages.manage_gates";
    }

    public static class StageTeams
    {
        public const string Manage = "stage_teams.manage";
    }

    /// <summary>Scope of work (نطاق العمل) — office only; the client role holds none of these.</summary>
    public static class Scope
    {
        public const string Read = "scope.read";
        public const string Manage = "scope.manage";
        public const string Confirm = "scope.confirm";

        /// <summary>The three, as <c>ProjectsModule</c> registers them, so a count can't drift from the
        /// constants.</summary>
        public static readonly IReadOnlyList<string> All = [Read, Manage, Confirm];
    }

    public static class StageTasks
    {
        public const string Read = "stage_tasks.read";
        public const string Manage = "stage_tasks.manage";
        public const string Assign = "stage_tasks.assign";
        public const string Complete = "stage_tasks.complete";
        public const string RequestApproval = "stage_tasks.request_approval";
        public const string DecideApproval = "stage_tasks.decide_approval";
        public const string ManageGates = "stage_tasks.manage_gates";
    }
}
