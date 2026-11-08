using SharedKernel;

namespace Projects.Contracts;

/// <summary>
/// Row-level access guard, ported from the backend's <c>IProjectAccessService</c>. The override is
/// <see cref="ProjectsPermissions.Projects.ReadAll"/> — no role/admin special-casing beyond that.
/// Otherwise access requires membership (see G5-05 and <see cref="IProjectAccessLevelReader"/> for the sources).
///
/// <para><b>P1-A: project access grants.</b> An active grant gives a non-member access to some
/// <see cref="ProjectAccessArea"/>s of one project, optionally only some stages. The rules, one for the whole
/// contract:</para>
/// <list type="bullet">
/// <item><b>Membership and <c>read_all</c> ignore area and stage.</b> They cover everything, as before.</item>
/// <item><b>Without an area</b> (the original overloads), any active grant counts, whatever its areas and stages:
/// "can this caller see the project at all". Only callers that need no more than that use them.</item>
/// <item><b>With an area</b>, a grant counts when its areas include <c>area</c> (a single flag) and it covers
/// <c>stageId</c>: a whole-project grant covers every stage; a stage-limited grant covers its own stages. A null
/// <c>stageId</c> means "the project as a whole" (a stage-less thing, or a project-level write), which a
/// stage-limited grant covers only for <see cref="ProjectAccessArea.Overview"/>.</item>
/// <item><b>Lists</b> of stage-bound rows (the project's stages, tasks, documents and folders) ask
/// <see cref="GetStageFilterAsync"/>, which also answers whether the caller may see the area at all, and keep only
/// the rows of the stages it returns.</item>
/// <item><b>Grant-only requests.</b> When the request's route permission came from a grant rather than the token
/// (<c>BuildingBlocks.ImpliedPermissions.RouteAuthorizedByImpliedPermission</c>), every method counts grants only:
/// membership and <c>read_all</c> are ignored, so a grant-implied code opens nothing outside the grant.</item>
/// </list>
/// </summary>
public interface IProjectAccessGuard
{
    Task<Result> EnsureProjectAccessAsync(Guid projectId, string userId, CancellationToken ct);

    /// <summary>Batch, mandatory — never call <see cref="EnsureProjectAccessAsync(Guid, string, CancellationToken)"/>
    /// in a loop over a list. One set-based query for the whole batch.</summary>
    Task<IReadOnlyList<Guid>> FilterAccessibleProjectsAsync(
        IReadOnlyCollection<Guid> projectIds, string userId, CancellationToken ct);

    /// <summary>P1-A: access to <paramref name="area"/> of the project, at <paramref name="stageId"/> (null: the
    /// project as a whole). See the type's remarks for when a grant counts.</summary>
    Task<Result> EnsureProjectAccessAsync(
        Guid projectId, string userId, ProjectAccessArea area, Guid? stageId, CancellationToken ct);

    /// <summary>P1-A batch sibling: the projects whose <paramref name="area"/> the caller reaches as a whole (stage
    /// null). One statement.</summary>
    Task<IReadOnlyList<Guid>> FilterAccessibleProjectsAsync(
        IReadOnlyCollection<Guid> projectIds, string userId, ProjectAccessArea area, CancellationToken ct);

    /// <summary>
    /// P1-A: which stages of <paramref name="area"/> a list may show. <c>null</c>: every row (membership,
    /// <c>read_all</c> or a whole-project grant). A non-empty set: only rows of those stages (access comes only from a
    /// stage-limited grant; stage-less rows are hidden too). An <b>empty</b> set: the caller cannot see the area at
    /// all.
    /// </summary>
    Task<IReadOnlySet<Guid>?> GetStageFilterAsync(Guid projectId, string userId, ProjectAccessArea area, CancellationToken ct);

    /// <summary>P1-A batch sibling of <see cref="GetStageFilterAsync"/>: every requested project is a key.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlySet<Guid>?>> GetStageFiltersAsync(
        IReadOnlyCollection<Guid> projectIds, string userId, ProjectAccessArea area, CancellationToken ct);
}
