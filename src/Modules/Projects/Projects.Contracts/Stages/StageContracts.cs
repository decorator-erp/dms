using BuildingBlocks.Messaging;

namespace Projects.Contracts.Stages;

/// <summary>
/// Runtime <c>ProjectStage</c> lifecycle. Every <see cref="IProjectScoped"/> command/query here carries
/// <c>ProjectId</c> explicitly (never resolved via a lookup inside the scope decorator) — the same shape
/// the backend's own <c>ISiteScoped</c> uses, matching how these routes nest under
/// <c>/api/projects/{projectId}/stages/...</c>.
/// </summary>
/// <remarks>E2 adds the template <see cref="StageId"/>, <see cref="Notes"/> and the four dates (UTC) after
/// the original fields, as the frontend's <c>ProjectStage</c> model names them.</remarks>
public sealed record ProjectStageResponse(
    Guid Id, Guid ProjectId, string Name, string? Description, int Order, StageStatus Status,
    decimal ProgressPercent, int CompletedTasks, int TotalTasks, decimal? Cost,
    Guid? StageId = null, string? Notes = null, DateTime? EstimatedStartDate = null, DateTime? EstimatedEndDate = null,
    DateTime? ActualStartDate = null, DateTime? ActualEndDate = null);

/// <summary>E2 (D2): a stage added by hand. <see cref="Order"/> is clamped into 1..n+1 and the others shift
/// to keep the order contiguous. With <see cref="TemplateStageId"/> the stage is materialised from that
/// template stage (its tasks, sub-tasks and department teams) exactly as a scope-of-work confirm does.</summary>
public sealed record CreateProjectStageCommand(
    Guid ProjectId, string UserId, string Name, int Order, string? Description, string? Notes,
    DateTime? EstimatedStartDate, DateTime? EstimatedEndDate, Guid? TemplateStageId)
    : ICommand<Guid>, IProjectScoped;

/// <summary>E2. A null <see cref="Order"/> keeps the stage where it is (FINDING-E-5).</summary>
public sealed record UpdateProjectStageCommand(
    Guid ProjectId, Guid StageId, string UserId, string Name, int? Order, string? Description, string? Notes,
    DateTime? EstimatedStartDate, DateTime? EstimatedEndDate)
    : ICommand, IProjectScoped;

public sealed record DeleteProjectStageCommand(Guid ProjectId, Guid StageId, string UserId)
    : ICommand, IProjectScoped;

public sealed record CompleteProjectStageCommand(Guid ProjectId, Guid StageId, string UserId)
    : ICommand, IProjectScoped;

public sealed record RejectProjectStageCommand(Guid ProjectId, Guid StageId, string UserId, string Reason)
    : ICommand, IProjectScoped;

public sealed record GetProjectStageQuery(Guid ProjectId, Guid StageId, string UserId)
    : IQuery<ProjectStageResponse>, IProjectScoped;

public sealed record GetProjectStagesByProjectQuery(Guid ProjectId, string UserId)
    : IQuery<IReadOnlyList<ProjectStageResponse>>, IProjectScoped;
