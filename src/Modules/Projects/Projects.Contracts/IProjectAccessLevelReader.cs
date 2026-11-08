namespace Projects.Contracts;

/// <summary>
/// P10 (C4) / C6: the caller's effective level on a project — the highest of
/// <list type="bullet">
/// <item><c>project.read_all</c> → <see cref="ProjectMemberAccess.Manager"/>;</item>
/// <item>the project's owner → Manager;</item>
/// <item>the head of one of its stage teams → Manager;</item>
/// <item>a project member row → that row's own level;</item>
/// <item>a seat on one of its stage teams → <see cref="ProjectMemberAccess.Editor"/>;</item>
/// <item>the assignee of one of its tasks → Editor;</item>
/// <item>P1-A: an active project access grant → its level, <b>only through the area overloads</b>.</item>
/// </list>
/// No level means no access at all: the same sources make up <see cref="IProjectAccessGuard"/>'s scope. Like the
/// guard, the <c>read_all</c> override is read from the current user and does not touch the projects table, so
/// a level is no proof the project exists.
///
/// <para><b>P1-A: the overloads without an area never count a grant.</b> They answer "what is the caller's level
/// as a member", which is what a write checks unless it names the area it writes to. So a write that has not been
/// taught its area stays membership-only and nothing gets wider. The area overloads add a grant that covers
/// <c>area</c> at <c>stageId</c>, with exactly <see cref="IProjectAccessGuard"/>'s rule (a null stage is the project
/// as a whole; a stage-limited grant covers it only for Overview), and its grant-only rule for requests whose route
/// permission a grant implied.</para>
/// </summary>
public interface IProjectAccessLevelReader
{
    /// <summary>Null when the user has no access to the project. Membership only (no grants).</summary>
    Task<ProjectMemberAccess?> GetLevelAsync(Guid projectId, string userId, CancellationToken ct);

    /// <summary>Batch sibling: one statement for the whole batch. Projects the user can't reach are absent.</summary>
    Task<IReadOnlyDictionary<Guid, ProjectMemberAccess>> GetLevelsAsync(
        IReadOnlyCollection<Guid> projectIds, string userId, CancellationToken ct);

    /// <summary>P1-A: membership, or an active grant covering <paramref name="area"/> at <paramref name="stageId"/>
    /// (null: the project as a whole). The highest wins.</summary>
    Task<ProjectMemberAccess?> GetLevelAsync(
        Guid projectId, string userId, ProjectAccessArea area, Guid? stageId, CancellationToken ct);

    /// <summary>P1-A batch sibling (stage null): one statement for the whole batch.</summary>
    Task<IReadOnlyDictionary<Guid, ProjectMemberAccess>> GetLevelsAsync(
        IReadOnlyCollection<Guid> projectIds, string userId, ProjectAccessArea area, CancellationToken ct);
}
