using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using Projects.Infrastructure.Persistence;
using Domain = Projects.Domain;

namespace Projects.Infrastructure.Services;

/// <summary>P10 (C4): see <see cref="IProjectAccessLevelReader"/> for the sources. One statement per call, single
/// or batch: <see cref="Levels"/> is a <c>UNION ALL</c> of the five per-project sources, grouped by project. P1-A: the
/// area overloads add the covering grants to the same <c>UNION ALL</c> (<see cref="ProjectAccessContext"/>).</summary>
internal sealed class ProjectAccessLevelReader(ProjectAccessContext access) : IProjectAccessLevelReader
{
    private const int Manager = (int)Domain.ProjectMemberAccess.Manager;
    private const int Editor = (int)Domain.ProjectMemberAccess.Editor;

    public Task<ProjectMemberAccess?> GetLevelAsync(Guid projectId, string userId, CancellationToken ct) =>
        GetOneAsync(projectId, userId, area: null, stageId: null, ct);

    public Task<IReadOnlyDictionary<Guid, ProjectMemberAccess>> GetLevelsAsync(
        IReadOnlyCollection<Guid> projectIds, string userId, CancellationToken ct) =>
        GetManyAsync(projectIds, userId, area: null, stageId: null, ct);

    public Task<ProjectMemberAccess?> GetLevelAsync(
        Guid projectId, string userId, ProjectAccessArea area, Guid? stageId, CancellationToken ct)
    {
        ProjectAccessContext.EnsureSingleArea(area);
        return GetOneAsync(projectId, userId, area, stageId, ct);
    }

    public Task<IReadOnlyDictionary<Guid, ProjectMemberAccess>> GetLevelsAsync(
        IReadOnlyCollection<Guid> projectIds, string userId, ProjectAccessArea area, CancellationToken ct)
    {
        ProjectAccessContext.EnsureSingleArea(area);
        return GetManyAsync(projectIds, userId, area, stageId: null, ct);
    }

    private async Task<ProjectMemberAccess?> GetOneAsync(
        Guid projectId, string userId, ProjectAccessArea? area, Guid? stageId, CancellationToken ct)
    {
        IReadOnlyDictionary<Guid, ProjectMemberAccess> levels = await GetManyAsync([projectId], userId, area, stageId, ct);
        return levels.TryGetValue(projectId, out ProjectMemberAccess level) ? level : null;
    }

    /// <summary>No area: membership only, never a grant (see the contract). With an area: membership plus the
    /// grants covering it at <paramref name="stageId"/>.</summary>
    private async Task<IReadOnlyDictionary<Guid, ProjectMemberAccess>> GetManyAsync(
        IReadOnlyCollection<Guid> projectIds, string userId, ProjectAccessArea? area, Guid? stageId, CancellationToken ct)
    {
        if (projectIds.Count == 0)
        {
            return new Dictionary<Guid, ProjectMemberAccess>();
        }

        if (access.ReadAll)
        {
            return projectIds.Distinct().ToDictionary(id => id, _ => ProjectMemberAccess.Manager);
        }

        Guid? employeeId = await access.ResolveEmployeeIdAsync(userId, ct);
        if (employeeId is null)
        {
            return new Dictionary<Guid, ProjectMemberAccess>();
        }

        IQueryable<ProjectLevelRow> sources = area is null
            ? access.MembershipLevels(employeeId.Value)
            : access.LevelsWithGrants(employeeId.Value, area, stageId);

        var rows = await sources
            .Where(l => projectIds.Contains(l.ProjectId))
            .GroupBy(l => l.ProjectId)
            .Select(g => new { ProjectId = g.Key, Level = g.Max(l => l.Level) })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.ProjectId, r => (ProjectMemberAccess)r.Level);
    }

    /// <summary>Every (project, level) the employee holds through a row of their own — one row per source, so a
    /// project can appear more than once. <see cref="ProjectAccessGuard.AccessibleProjectIds"/> is its projects.</summary>
    internal static IQueryable<ProjectLevelRow> Levels(ProjectsDbContext db, Guid employeeId) =>
        db.Projects.AsNoTracking()
            .Where(p => p.OwnerId == employeeId)
            .Select(p => new ProjectLevelRow { ProjectId = p.Id, Level = Manager })
            .Concat(db.StageTeams.AsNoTracking()
                .Where(t => t.HeadId == employeeId)
                .Join(db.ProjectStages, t => t.ProjectStageId, s => s.Id,
                    (t, s) => new ProjectLevelRow { ProjectId = s.ProjectId, Level = Manager }))
            .Concat(db.ProjectMembers.AsNoTracking()
                .Where(m => m.EmployeeId == employeeId)
                .Select(m => new ProjectLevelRow { ProjectId = m.ProjectId, Level = (int)m.Access }))
            .Concat(db.StageTeamMembers.AsNoTracking()
                .Where(m => m.EmployeeId == employeeId)
                .Join(db.StageTeams, m => m.StageTeamId, t => t.Id, (m, t) => t)
                .Join(db.ProjectStages, t => t.ProjectStageId, s => s.Id,
                    (t, s) => new ProjectLevelRow { ProjectId = s.ProjectId, Level = Editor }))
            .Concat(db.ProjectTasks.AsNoTracking()
                .Where(t => t.AssignedToId == employeeId)
                .Select(t => new ProjectLevelRow { ProjectId = t.ProjectId, Level = Editor }));

    internal sealed class ProjectLevelRow
    {
        public Guid ProjectId { get; init; }
        public int Level { get; init; }
    }
}
