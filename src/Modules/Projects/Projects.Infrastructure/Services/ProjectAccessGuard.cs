using BuildingBlocks;
using Hr.Contracts;
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
/// <see cref="IHrDirectory.ResolveEmployeeIdAsync"/> first, which already memoizes per DI scope.
/// </summary>
internal sealed class ProjectAccessGuard(ProjectsDbContext db, IHrDirectory hrDirectory, ICurrentUser currentUser)
    : IProjectAccessGuard
{
    public async Task<Result> EnsureProjectAccessAsync(Guid projectId, string userId, CancellationToken ct)
    {
        if (currentUser.HasPermission(ProjectsPermissions.Projects.ReadAll))
        {
            return Result.Success();
        }

        Guid? employeeId = await hrDirectory.ResolveEmployeeIdAsync(userId, ct);
        if (employeeId is null)
        {
            return Domain.ProjectErrors.AccessDenied;
        }

        bool hasAccess = await AccessibleProjectIds(db, employeeId.Value).AnyAsync(id => id == projectId, ct);

        return hasAccess ? Result.Success() : Domain.ProjectErrors.AccessDenied;
    }

    /// <summary>One set-based query for the whole batch — never a loop calling
    /// <see cref="EnsureProjectAccessAsync"/> per id.</summary>
    public async Task<IReadOnlyList<Guid>> FilterAccessibleProjectsAsync(
        IReadOnlyCollection<Guid> projectIds, string userId, CancellationToken ct)
    {
        if (projectIds.Count == 0)
        {
            return [];
        }

        if (currentUser.HasPermission(ProjectsPermissions.Projects.ReadAll))
        {
            return [.. projectIds];
        }

        Guid? employeeId = await hrDirectory.ResolveEmployeeIdAsync(userId, ct);
        if (employeeId is null)
        {
            return [];
        }

        return await AccessibleProjectIds(db, employeeId.Value)
            .Where(id => projectIds.Contains(id))
            .Distinct()
            .ToListAsync(ct);
    }

    /// <summary>D-14 (C-F2, owner default): the projects the employee owns, plus those with a stage team
    /// they are on. The owner always has access — a FreeStyle project with no team no longer hides itself
    /// (and its client) from the person who created it.</summary>
    /// <remarks>Shared with <c>GetProjectsQueryHandler</c>, whose paged list can't use the batch method
    /// (it has no candidate ids before filtering) — one rule, one query (FINDING-P1-1).</remarks>
    internal static IQueryable<Guid> AccessibleProjectIds(ProjectsDbContext db, Guid employeeId) =>
        db.Projects.AsNoTracking()
            .Where(p => p.OwnerId == employeeId)
            .Select(p => p.Id)
            .Union(db.StageTeamMembers.AsNoTracking()
                .Where(m => m.EmployeeId == employeeId)
                .Join(db.StageTeams, m => m.StageTeamId, t => t.Id, (m, t) => t)
                .Join(db.ProjectStages, t => t.ProjectStageId, s => s.Id, (t, s) => s.ProjectId));
}
