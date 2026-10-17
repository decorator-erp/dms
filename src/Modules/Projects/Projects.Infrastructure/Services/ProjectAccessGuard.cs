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
/// beyond that. Otherwise access requires stage-team membership, checked against Projects-owned tables
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

        bool isMember = await MembershipQuery(employeeId.Value).AnyAsync(s => s.ProjectId == projectId, ct);

        return isMember ? Result.Success() : Domain.ProjectErrors.AccessDenied;
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

        return await MembershipQuery(employeeId.Value)
            .Where(s => projectIds.Contains(s.ProjectId))
            .Select(s => s.ProjectId)
            .Distinct()
            .ToListAsync(ct);
    }

    private IQueryable<Domain.ProjectStage> MembershipQuery(Guid employeeId) =>
        db.StageTeamMembers.AsNoTracking()
            .Where(m => m.EmployeeId == employeeId)
            .Join(db.StageTeams, m => m.StageTeamId, t => t.Id, (m, t) => t)
            .Join(db.ProjectStages, t => t.ProjectStageId, s => s.Id, (t, s) => s);
}
