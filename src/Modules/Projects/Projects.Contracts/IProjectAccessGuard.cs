using SharedKernel;

namespace Projects.Contracts;

/// <summary>
/// Row-level access guard, ported from the backend's <c>IProjectAccessService</c>. The override is
/// <see cref="ProjectsPermissions.Projects.ReadAll"/> — no role/admin special-casing beyond that.
/// Otherwise access requires stage-team membership (see G5-05).
/// </summary>
public interface IProjectAccessGuard
{
    Task<Result> EnsureProjectAccessAsync(Guid projectId, string userId, CancellationToken ct);

    /// <summary>Batch, mandatory — never call <see cref="EnsureProjectAccessAsync"/> in a loop over a
    /// list. One set-based membership query for the whole batch.</summary>
    Task<IReadOnlyList<Guid>> FilterAccessibleProjectsAsync(
        IReadOnlyCollection<Guid> projectIds, string userId, CancellationToken ct);
}
