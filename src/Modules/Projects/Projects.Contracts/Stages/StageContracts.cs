using BuildingBlocks.Messaging;

namespace Projects.Contracts.Stages;

/// <summary>
/// Runtime <c>ProjectStage</c> lifecycle. Every <see cref="IProjectScoped"/> command/query here carries
/// <c>ProjectId</c> explicitly (never resolved via a lookup inside the scope decorator) — the same shape
/// the backend's own <c>ISiteScoped</c> uses, matching how these routes nest under
/// <c>/api/projects/{projectId}/stages/...</c>.
/// </summary>
public sealed record ProjectStageResponse(
    Guid Id, Guid ProjectId, string Name, string? Description, int Order, StageStatus Status,
    decimal ProgressPercent, int CompletedTasks, int TotalTasks, decimal? Cost);

public sealed record CompleteProjectStageCommand(Guid ProjectId, Guid StageId, string UserId)
    : ICommand, IProjectScoped;

public sealed record RejectProjectStageCommand(Guid ProjectId, Guid StageId, string UserId, string Reason)
    : ICommand, IProjectScoped;

public sealed record GetProjectStageQuery(Guid ProjectId, Guid StageId, string UserId)
    : IQuery<ProjectStageResponse>, IProjectScoped;

public sealed record GetProjectStagesByProjectQuery(Guid ProjectId, string UserId)
    : IQuery<IReadOnlyList<ProjectStageResponse>>, IProjectScoped;
