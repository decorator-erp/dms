namespace Projects.Contracts;

/// <summary>
/// P10 (C4) / C6: the caller's effective level on a project — the highest of
/// <list type="bullet">
/// <item><c>project.read_all</c> → <see cref="ProjectMemberAccess.Manager"/>;</item>
/// <item>the project's owner → Manager;</item>
/// <item>the head of one of its stage teams → Manager;</item>
/// <item>a project member row → that row's own level;</item>
/// <item>a seat on one of its stage teams → <see cref="ProjectMemberAccess.Editor"/>;</item>
/// <item>the assignee of one of its tasks → Editor.</item>
/// </list>
/// No level means no access at all: the same sources make up <see cref="IProjectAccessGuard"/>'s scope. Like the
/// guard, the <c>read_all</c> override is read from the current user and does not touch the projects table, so
/// a level is no proof the project exists.
/// </summary>
public interface IProjectAccessLevelReader
{
    /// <summary>Null when the user has no access to the project.</summary>
    Task<ProjectMemberAccess?> GetLevelAsync(Guid projectId, string userId, CancellationToken ct);

    /// <summary>Batch sibling: one statement for the whole batch. Projects the user can't reach are absent.</summary>
    Task<IReadOnlyDictionary<Guid, ProjectMemberAccess>> GetLevelsAsync(
        IReadOnlyCollection<Guid> projectIds, string userId, CancellationToken ct);
}
