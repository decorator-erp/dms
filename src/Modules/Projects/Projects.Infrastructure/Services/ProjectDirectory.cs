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
            return [];

        return await db.Projects.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(ToProjectSummary()).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProjectSummary>> GetProjectsByClientAsync(Guid clientId, CancellationToken ct) =>
        await db.Projects.AsNoTracking().Where(p => p.ClientId == clientId).Select(ToProjectSummary()).ToListAsync(ct);

    public async Task<IReadOnlyList<ProjectSummary>> GetProjectsByClientsAsync(
        IReadOnlyCollection<Guid> clientIds, CancellationToken ct)
    {
        if (clientIds.Count == 0)
            return [];

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
            return [];

        return await db.ProjectStages.AsNoTracking().Where(s => ids.Contains(s.Id)).Select(ToStageSummary()).ToListAsync(ct);
    }

    public async Task<TaskSummary?> GetTaskAsync(Guid taskId, CancellationToken ct) =>
        await db.ProjectTasks.AsNoTracking().Where(t => t.Id == taskId).Select(ToTaskSummary()).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<TaskSummary>> GetTasksAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return [];

        return await db.ProjectTasks.AsNoTracking().Where(t => ids.Contains(t.Id)).Select(ToTaskSummary()).ToListAsync(ct);
    }

    private static System.Linq.Expressions.Expression<Func<Domain.Project, ProjectSummary>> ToProjectSummary() => p =>
        new ProjectSummary(p.Id, p.Code, p.Name, ProjectsEnumMappingShim.ToContractsStatus(p.Status), p.ClientId);

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
