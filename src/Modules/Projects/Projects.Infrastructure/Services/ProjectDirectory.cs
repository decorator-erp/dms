using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using Projects.Infrastructure.Persistence;
using Domain = Projects.Domain;

namespace Projects.Infrastructure.Services;

/// <summary>Implements <see cref="IProjectDirectory"/> over <see cref="ProjectsDbContext"/> — always via
/// a <c>Select</c> projection straight to the summary DTO, never load-then-map, same convention
/// <c>HrDirectory</c> established at G2-06.</summary>
internal sealed class ProjectDirectory(ProjectsDbContext db) : IProjectDirectory
{
    public async Task<ProjectSummary?> GetProjectAsync(Guid projectId, CancellationToken ct) =>
        await db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(ToProjectSummary()).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ProjectSummary>> GetProjectsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Projects.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(ToProjectSummary()).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProjectSummary>> GetProjectsByClientAsync(Guid clientId, CancellationToken ct) =>
        await db.Projects.AsNoTracking().Where(p => p.ClientId == clientId).Select(ToProjectSummary()).ToListAsync(ct);

    public async Task<IReadOnlyList<ProjectSummary>> GetProjectsByClientsAsync(
        IReadOnlyCollection<Guid> clientIds, CancellationToken ct)
    {
        if (clientIds.Count == 0)
        {
            return [];
        }

        return await db.Projects.AsNoTracking()
            .Where(p => clientIds.Contains(p.ClientId))
            .Select(ToProjectSummary())
            .ToListAsync(ct);
    }

    public async Task<StageSummary?> GetStageAsync(Guid stageId, CancellationToken ct) =>
        await db.ProjectStages.AsNoTracking().Where(s => s.Id == stageId).Select(ToStageSummary()).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<StageSummary>> GetStagesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.ProjectStages.AsNoTracking().Where(s => ids.Contains(s.Id)).Select(ToStageSummary()).ToListAsync(ct);
    }

    public async Task<TaskSummary?> GetTaskAsync(Guid taskId, CancellationToken ct) =>
        await db.ProjectTasks.AsNoTracking().Where(t => t.Id == taskId).Select(ToTaskSummary()).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<TaskSummary>> GetTasksAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.ProjectTasks.AsNoTracking().Where(t => ids.Contains(t.Id)).Select(ToTaskSummary()).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TaskSummary>> GetOpenTasksAssignedToAsync(
        IReadOnlyCollection<Guid> employeeIds, int max, CancellationToken ct)
    {
        if (employeeIds.Count == 0 || max <= 0)
        {
            return [];
        }

        IQueryable<Guid> activeProjects = db.Projects.AsNoTracking().Where(p => p.IsActive).Select(p => p.Id);
        return await db.ProjectTasks.AsNoTracking()
            .Where(t => t.AssignedToId != null && employeeIds.Contains(t.AssignedToId.Value)
                && t.Status != Domain.ProjectTaskStatus.Completed && t.Status != Domain.ProjectTaskStatus.Cancelled
                && activeProjects.Contains(t.ProjectId))
            .OrderBy(t => t.ProjectId).ThenBy(t => t.Order).ThenBy(t => t.Id)
            .Take(max)
            .Select(ToTaskSummary())
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProjectProgressSummary>> GetProgressAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken ct)
    {
        if (projectIds.Count == 0)
        {
            return [];
        }

        // P2/NP5: computed on read from the stages' recounted counters plus the unstaged tasks (ProjectProgress).
        var projects = await db.Projects.AsNoTracking()
            .Where(p => projectIds.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                Completed = db.ProjectStages.Where(s => s.ProjectId == p.Id).Sum(s => s.CompletedTasks)
                    + db.ProjectTasks.Count(t => t.ProjectId == p.Id && t.ProjectStageId == null && t.Status == Domain.ProjectTaskStatus.Completed),
                Total = db.ProjectStages.Where(s => s.ProjectId == p.Id).Sum(s => s.TotalTasks)
                    + db.ProjectTasks.Count(t => t.ProjectId == p.Id && t.ProjectStageId == null && t.Status != Domain.ProjectTaskStatus.Cancelled),
            })
            .ToListAsync(ct);
        var active = await db.ProjectStages.AsNoTracking()
            .Where(s => projectIds.Contains(s.ProjectId) && s.Status == Domain.StageStatus.Active)
            .Select(s => new { s.ProjectId, s.Id, s.Name, s.Order })
            .ToListAsync(ct);
        var firstActive = active.GroupBy(s => s.ProjectId).ToDictionary(g => g.Key, g => g.OrderBy(s => s.Order).First());

        return
        [
            .. projects.Select(p => firstActive.TryGetValue(p.Id, out var stage)
                ? new ProjectProgressSummary(p.Id, ProjectProgress.Percent(p.Completed, p.Total), stage.Id, stage.Name)
                : new ProjectProgressSummary(p.Id, ProjectProgress.Percent(p.Completed, p.Total), null, null)),
        ];
    }

    private static System.Linq.Expressions.Expression<Func<Domain.Project, ProjectSummary>> ToProjectSummary() => p =>
        new ProjectSummary(p.Id, p.Code, p.Name, ProjectsEnumMappingShim.ToContractsStatus(p.Status), p.ClientId, p.OwnerId, p.IsActive);

    private static System.Linq.Expressions.Expression<Func<Domain.ProjectStage, StageSummary>> ToStageSummary() => s =>
        new StageSummary(s.Id, s.ProjectId, s.Name, ProjectsEnumMappingShim.ToContractsStageStatus(s.Status));

    private static System.Linq.Expressions.Expression<Func<Domain.ProjectTask, TaskSummary>> ToTaskSummary() => t =>
        new TaskSummary(t.Id, t.ProjectId, t.ProjectStageId, t.Name, ProjectsEnumMappingShim.ToContractsTaskStatus(t.Status), t.AssignedToId);
}

/// <summary>
/// <c>Projects.Infrastructure</c> cannot reference <c>Projects.Application</c>'s internal
/// <c>ProjectsEnumMapping</c> (no <c>InternalsVisibleTo</c> grant, same reasoning as
/// <c>ProjectScopeRegistration</c>'s own remarks) - a tiny local numeric-cast mirror, same trade-off
/// <c>ProjectsEnumMapping</c> itself documents, kept private to this one file since it's the only
/// Infrastructure-side consumer.
/// </summary>
file static class ProjectsEnumMappingShim
{
    public static ProjectStatus ToContractsStatus(Domain.ProjectStatus value) => (ProjectStatus)value;
    public static StageStatus ToContractsStageStatus(Domain.StageStatus value) => (StageStatus)value;
    public static ProjectTaskStatus ToContractsTaskStatus(Domain.ProjectTaskStatus value) => (ProjectTaskStatus)value;
}
