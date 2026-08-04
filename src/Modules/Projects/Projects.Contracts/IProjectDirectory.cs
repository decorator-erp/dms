namespace Projects.Contracts;

public sealed record ProjectSummary(Guid Id, string Code, string Name, ProjectStatus Status, Guid ClientId);

public sealed record StageSummary(Guid Id, Guid ProjectId, string Name, StageStatus Status);

public sealed record TaskSummary(
    Guid Id, Guid ProjectId, Guid? ProjectStageId, string Name, ProjectTaskStatus Status, Guid? AssignedToId);

/// <summary>Cross-module read surface for Projects-owned data. Every lookup has a batch sibling — no
/// exceptions, per <c>docs/adding-a-module.md</c>'s Contracts rule.</summary>
public interface IProjectDirectory
{
    Task<ProjectSummary?> GetProjectAsync(Guid projectId, CancellationToken ct);
    Task<IReadOnlyList<ProjectSummary>> GetProjectsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    Task<StageSummary?> GetStageAsync(Guid stageId, CancellationToken ct);
    Task<IReadOnlyList<StageSummary>> GetStagesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    Task<TaskSummary?> GetTaskAsync(Guid taskId, CancellationToken ct);
    Task<IReadOnlyList<TaskSummary>> GetTasksAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
