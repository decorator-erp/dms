using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Infrastructure.Services;

/// <summary>
/// Ported from the backend's <c>ProjectAccessService</c> — a permission-based row-level access guard.
/// The override is <see cref="ProjectsPermissions.Projects.ReadAll"/>: no role/admin special-casing
/// beyond that. Otherwise access requires owning the project (D-14) or stage-team membership, checked against Projects-owned tables
/// only (<c>StageTeamMembers</c>/<c>StageTeams</c>/<c>ProjectStages</c>) — never a query into HR's own
/// tables; the caller's identity is resolved to an <c>Employee</c> id via
/// <c>IHrDirectory.ResolveEmployeeIdAsync</c> first, which already memoizes per DI scope.
///
/// <para>P1-A: plus active project access grants, by the rules on <see cref="IProjectAccessGuard"/>; the sources are
/// read through <see cref="ProjectAccessContext"/>, as the level reader's are. Each check is still one statement.</para>
/// </summary>
internal sealed class ProjectAccessGuard(ProjectAccessContext access) : IProjectAccessGuard
{
    public Task<Result> EnsureProjectAccessAsync(Guid projectId, string userId, CancellationToken ct) =>
        EnsureAsync(projectId, userId, area: null, stageId: null, ct);

    public Task<Result> EnsureProjectAccessAsync(
        Guid projectId, string userId, ProjectAccessArea area, Guid? stageId, CancellationToken ct)
    {
        ProjectAccessContext.EnsureSingleArea(area);
        return EnsureAsync(projectId, userId, area, stageId, ct);
    }

    /// <summary>One set-based query for the whole batch — never a loop calling
    /// <see cref="EnsureProjectAccessAsync(Guid, string, CancellationToken)"/> per id.</summary>
    public Task<IReadOnlyList<Guid>> FilterAccessibleProjectsAsync(
        IReadOnlyCollection<Guid> projectIds, string userId, CancellationToken ct) =>
        FilterAsync(projectIds, userId, area: null, ct);

    public Task<IReadOnlyList<Guid>> FilterAccessibleProjectsAsync(
        IReadOnlyCollection<Guid> projectIds, string userId, ProjectAccessArea area, CancellationToken ct)
    {
        ProjectAccessContext.EnsureSingleArea(area);
        return FilterAsync(projectIds, userId, area, ct);
    }

    public async Task<IReadOnlySet<Guid>?> GetStageFilterAsync(
        Guid projectId, string userId, ProjectAccessArea area, CancellationToken ct)
    {
        IReadOnlyDictionary<Guid, IReadOnlySet<Guid>?> filters = await GetStageFiltersAsync([projectId], userId, area, ct);
        return filters[projectId];
    }

    /// <summary>Two statements whatever the batch size: which projects the caller reaches by membership, then their
    /// active grants on the others.</summary>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlySet<Guid>?>> GetStageFiltersAsync(
        IReadOnlyCollection<Guid> projectIds, string userId, ProjectAccessArea area, CancellationToken ct)
    {
        ProjectAccessContext.EnsureSingleArea(area);
        var result = projectIds.Distinct().ToDictionary(id => id, IReadOnlySet<Guid>? (_) => new HashSet<Guid>());
        if (result.Count == 0)
        {
            return result;
        }

        if (access.ReadAll)
        {
            return result.Keys.ToDictionary(id => id, IReadOnlySet<Guid>? (_) => null);
        }

        Guid? employeeId = await access.ResolveEmployeeIdAsync(userId, ct);
        if (employeeId is null)
        {
            return result;
        }

        HashSet<Guid> members = access.GrantOnly
            ? []
            : [.. await ProjectAccessLevelReader.Levels(access.Db, employeeId.Value)
                .Where(l => projectIds.Contains(l.ProjectId))
                .Select(l => l.ProjectId)
                .Distinct()
                .ToListAsync(ct)];

        List<Guid> others = [.. result.Keys.Where(id => !members.Contains(id))];
        List<Domain.ProjectAccessGrant> grants = others.Count == 0
            ? []
            : await access.ActiveGrants(employeeId.Value).Where(g => others.Contains(g.ProjectId)).ToListAsync(ct);

        foreach (Guid id in members)
        {
            result[id] = null;
        }

        foreach (Guid id in others)
        {
            result[id] = ProjectAccessContext.StageFilter(grants.FirstOrDefault(g => g.ProjectId == id), area);
        }

        return result;
    }

    private async Task<Result> EnsureAsync(
        Guid projectId, string userId, ProjectAccessArea? area, Guid? stageId, CancellationToken ct)
    {
        if (access.ReadAll)
        {
            return Result.Success();
        }

        Guid? employeeId = await access.ResolveEmployeeIdAsync(userId, ct);
        if (employeeId is null)
        {
            return Domain.ProjectErrors.AccessDenied;
        }

        bool hasAccess = await access.LevelsWithGrants(employeeId.Value, area, stageId)
            .AnyAsync(l => l.ProjectId == projectId, ct);

        return hasAccess ? Result.Success() : Domain.ProjectErrors.AccessDenied;
    }

    private async Task<IReadOnlyList<Guid>> FilterAsync(
        IReadOnlyCollection<Guid> projectIds, string userId, ProjectAccessArea? area, CancellationToken ct)
    {
        if (projectIds.Count == 0)
        {
            return [];
        }

        if (access.ReadAll)
        {
            return [.. projectIds];
        }

        Guid? employeeId = await access.ResolveEmployeeIdAsync(userId, ct);
        if (employeeId is null)
        {
            return [];
        }

        return await access.LevelsWithGrants(employeeId.Value, area, stageId: null)
            .Select(l => l.ProjectId)
            .Where(id => projectIds.Contains(id))
            .Distinct()
            .ToListAsync(ct);
    }

    /// <summary>D-14 (C-F2, owner default) and E1 (D1): the projects the employee owns, those with a stage
    /// team they are on, and those they are a project member of (any access level). The owner always has access — a FreeStyle project with no team no longer hides itself
    /// (and its client) from the person who created it. P6/NP6: also the projects with a stage team the employee
    /// heads (a head is not on the team's member list) and those with a task assigned to them. P10: the projects
    /// <see cref="ProjectAccessLevelReader.Levels"/> gives a level on, so scope and level can't disagree. P1-A: plus
    /// the projects of the employee's active grants, any area (grants only on a grant-only request).</summary>
    /// <remarks>Shared with <c>GetProjectsQueryHandler</c>, whose paged list can't use the batch method
    /// (it has no candidate ids before filtering) — one rule, one query (FINDING-P1-1).</remarks>
    internal static IQueryable<Guid> AccessibleProjectIds(ProjectAccessContext access, Guid employeeId) =>
        access.LevelsWithGrants(employeeId, area: null, stageId: null).Select(l => l.ProjectId).Distinct();

    /// <summary>E8 / C7 <c>mine=true</c>: the projects the employee owns or is a member of (D1). A stage-team
    /// seat alone doesn't make a project "mine"; it still makes it accessible. A grant never does.</summary>
    internal static IQueryable<Guid> MyProjectIds(ProjectsDbContext db, Guid employeeId) =>
        db.Projects.AsNoTracking()
            .Where(p => p.OwnerId == employeeId)
            .Select(p => p.Id)
            .Union(db.ProjectMembers.AsNoTracking()
                .Where(m => m.EmployeeId == employeeId)
                .Select(m => m.ProjectId));
}
