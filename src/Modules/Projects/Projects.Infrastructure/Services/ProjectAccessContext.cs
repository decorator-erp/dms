using BuildingBlocks;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using Projects.Contracts.Grants;
using Projects.Infrastructure.Persistence;
using Domain = Projects.Domain;

namespace Projects.Infrastructure.Services;

/// <summary>
/// P1-A: the one place the access sources are read — membership (<see cref="ProjectAccessLevelReader.Levels"/>),
/// <c>project.read_all</c>, and active grants — shared by <see cref="ProjectAccessGuard"/>,
/// <see cref="ProjectAccessLevelReader"/>, <see cref="ProjectGrantPermissions"/> and the project read handlers, so
/// they cannot disagree. See <see cref="IProjectAccessGuard"/> for the rules.
///
/// <para>The clock is <see cref="TimeProvider"/>: a grant's window is checked against it on every call, so an
/// expired grant stops counting on the next request with no job.</para>
/// </summary>
internal sealed class ProjectAccessContext(
    ProjectsDbContext db, IHrDirectory hrDirectory, ICurrentUser currentUser, TimeProvider timeProvider,
    ImpliedPermissions impliedPermissions)
{
    public ProjectsDbContext Db => db;

    public DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>The request's route permission came from a grant, not the token: count grants only (no membership,
    /// no <c>read_all</c>), so an implied code opens nothing outside the grant.</summary>
    public bool GrantOnly => impliedPermissions.RouteAuthorizedByImpliedPermission;

    /// <summary><c>project.read_all</c> from the token, unless the request is grant-only.</summary>
    public bool ReadAll => !GrantOnly && currentUser.HasPermission(ProjectsPermissions.Projects.ReadAll);

    public Task<Guid?> ResolveEmployeeIdAsync(string userId, CancellationToken ct) =>
        hrDirectory.ResolveEmployeeIdAsync(userId, ct);

    /// <summary>Membership rows unless grant-only.</summary>
    public IQueryable<ProjectAccessLevelReader.ProjectLevelRow> MembershipLevels(Guid employeeId) =>
        GrantOnly
            ? ProjectAccessLevelReader.Levels(db, employeeId).Where(_ => false)
            : ProjectAccessLevelReader.Levels(db, employeeId);

    /// <summary>The employee's active grants, now.</summary>
    public IQueryable<Domain.ProjectAccessGrant> ActiveGrants(Guid employeeId) =>
        db.ProjectAccessGrants.AsNoTracking()
            .Where(g => g.EmployeeId == employeeId)
            .Where(Domain.ProjectAccessGrant.ActiveAt(UtcNow));

    /// <summary>Active grants covering <paramref name="area"/> at <paramref name="stageId"/>, or any active grant
    /// when <paramref name="area"/> is null. The SQL form of <see cref="Covers"/>.</summary>
    public IQueryable<Domain.ProjectAccessGrant> CoveringGrants(Guid employeeId, ProjectAccessArea? area, Guid? stageId)
    {
        IQueryable<Domain.ProjectAccessGrant> grants = ActiveGrants(employeeId);
        if (area is not { } wanted)
        {
            return grants;
        }

        Domain.ProjectAccessArea flag = ToDomain(wanted);
        grants = grants.Where(g => (g.Areas & flag) == flag);
        if (stageId is { } stage)
        {
            return grants.Where(g => g.StageIds.Count == 0 || g.StageIds.Contains(stage));
        }

        return wanted == ProjectAccessArea.Overview ? grants : grants.Where(g => g.StageIds.Count == 0);
    }

    /// <summary>Membership (unless grant-only) plus the grants <see cref="CoveringGrants"/> returns, as one
    /// <c>UNION ALL</c>.</summary>
    public IQueryable<ProjectAccessLevelReader.ProjectLevelRow> LevelsWithGrants(
        Guid employeeId, ProjectAccessArea? area, Guid? stageId) =>
        MembershipLevels(employeeId).Concat(CoveringGrants(employeeId, area, stageId)
            .Select(g => new ProjectAccessLevelReader.ProjectLevelRow { ProjectId = g.ProjectId, Level = (int)g.Level }));

    /// <summary>The in-memory rule <see cref="CoveringGrants"/> translates.</summary>
    public static bool Covers(Domain.ProjectAccessGrant grant, ProjectAccessArea area, Guid? stageId)
    {
        Domain.ProjectAccessArea flag = ToDomain(area);
        if ((grant.Areas & flag) != flag)
        {
            return false;
        }

        return grant.StageIds.Count == 0
            || (stageId is { } stage ? grant.StageIds.Contains(stage) : area == ProjectAccessArea.Overview);
    }

    /// <summary>A grant's stage filter for a list of <paramref name="area"/>: null for a whole-project grant, its
    /// stages otherwise, empty when it does not cover the area.</summary>
    public static IReadOnlySet<Guid>? StageFilter(Domain.ProjectAccessGrant? grant, ProjectAccessArea area)
    {
        Domain.ProjectAccessArea flag = ToDomain(area);
        if (grant is null || (grant.Areas & flag) != flag)
        {
            return new HashSet<Guid>();
        }

        return grant.StageIds.Count == 0 ? null : grant.StageIds.ToHashSet();
    }

    /// <summary>
    /// <see cref="ProjectAccessResponse"/> for each project the caller reaches (absent: no access). One membership
    /// statement and one grant statement for the whole batch. <c>read_all</c> wins, then membership, then a grant.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, ProjectAccessResponse>> GetAccessAsync(
        IReadOnlyCollection<Guid> projectIds, string userId, CancellationToken ct)
    {
        Dictionary<Guid, ProjectAccessResponse> result = [];
        if (projectIds.Count == 0)
        {
            return result;
        }

        if (ReadAll)
        {
            foreach (Guid id in projectIds.Distinct())
            {
                result[id] = new ProjectAccessResponse(
                    ProjectAccessResponse.ReadAllSource, ProjectMemberAccess.Manager, AllAreas, [], null);
            }

            return result;
        }

        Guid? employeeId = await ResolveEmployeeIdAsync(userId, ct);
        if (employeeId is null)
        {
            return result;
        }

        Dictionary<Guid, int> membership = GrantOnly
            ? []
            : await ProjectAccessLevelReader.Levels(db, employeeId.Value)
                .Where(l => projectIds.Contains(l.ProjectId))
                .GroupBy(l => l.ProjectId)
                .Select(g => new { ProjectId = g.Key, Level = g.Max(l => l.Level) })
                .ToDictionaryAsync(r => r.ProjectId, r => r.Level, ct);

        List<Domain.ProjectAccessGrant> grants = await ActiveGrants(employeeId.Value)
            .Where(g => projectIds.Contains(g.ProjectId))
            .ToListAsync(ct);

        foreach ((Guid projectId, int level) in membership)
        {
            result[projectId] = new ProjectAccessResponse(
                ProjectAccessResponse.MemberSource, (ProjectMemberAccess)level, AllAreas, [], null);
        }

        foreach (Domain.ProjectAccessGrant grant in grants.Where(g => !result.ContainsKey(g.ProjectId)))
        {
            result[grant.ProjectId] = new ProjectAccessResponse(
                ProjectAccessResponse.GrantSource, (ProjectMemberAccess)grant.Level, (ProjectAccessArea)grant.Areas,
                [.. grant.StageIds], grant.ExpiresAtUtc);
        }

        return result;
    }

    public const ProjectAccessArea AllAreas =
        ProjectAccessArea.Overview | ProjectAccessArea.StagesAndTasks | ProjectAccessArea.Documents
        | ProjectAccessArea.Sites | ProjectAccessArea.Crm | ProjectAccessArea.Team;

    public static Domain.ProjectAccessArea ToDomain(ProjectAccessArea area) => (Domain.ProjectAccessArea)area;

    /// <summary>The area overloads take exactly one area.</summary>
    public static void EnsureSingleArea(ProjectAccessArea area)
    {
        if (area == ProjectAccessArea.None || ((int)area & ((int)area - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(area), area, "Pass exactly one project access area.");
        }
    }
}
