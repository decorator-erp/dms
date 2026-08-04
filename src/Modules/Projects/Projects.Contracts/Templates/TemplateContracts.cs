using BuildingBlocks.Messaging;

namespace Projects.Contracts.Templates;

/// <summary>Template-side CRUD — governed by <c>ProjectsPermissions.Stages.Manage</c>/<c>Read</c>, per
/// the backend's own reuse of that permission group for template entities (there is no separate
/// template permission group).</summary>
public sealed record StageTemplateResponse(Guid Id, string Name, string? Description, StageTemplateType Type, bool IsDefault);

public sealed record CreateStageTemplateCommand(string Name, StageTemplateType Type, bool IsDefault = false, string? Description = null)
    : ICommand<Guid>;

public sealed record UpdateStageTemplateCommand(Guid StageTemplateId, string? Name = null, string? Description = null, bool? IsDefault = null)
    : ICommand;

public sealed record DeleteStageTemplateCommand(Guid StageTemplateId) : ICommand;

public sealed record GetStageTemplateQuery(Guid StageTemplateId) : IQuery<StageTemplateResponse>;

public sealed record GetStageTemplatesQuery : IQuery<IReadOnlyList<StageTemplateResponse>>;

public sealed record StageResponse(Guid Id, Guid StageTemplateId, string Name, string? Description, int Order, decimal WeightPercent);

public sealed record CreateStageCommand(Guid StageTemplateId, string Name, int Order, decimal WeightPercent, string? Description = null)
    : ICommand<Guid>;

public sealed record UpdateStageCommand(Guid StageId, string? Name = null, int? Order = null, decimal? WeightPercent = null, string? Description = null)
    : ICommand;

public sealed record DeleteStageCommand(Guid StageId) : ICommand;

public sealed record GetStagesByTemplateQuery(Guid StageTemplateId) : IQuery<IReadOnlyList<StageResponse>>;

public sealed record StageTaskResponse(Guid Id, Guid StageId, string Name, string? Description, int Order, decimal Weight, Guid? DepartmentId);

public sealed record CreateStageTaskCommand(Guid StageId, string Name, int Order, decimal Weight = 1.00m, string? Description = null, Guid? DepartmentId = null)
    : ICommand<Guid>;

public sealed record UpdateStageTaskCommand(
    Guid StageTaskId, string? Name = null, int? Order = null, decimal? Weight = null, string? Description = null, Guid? DepartmentId = null)
    : ICommand;

public sealed record DeleteStageTaskCommand(Guid StageTaskId) : ICommand;

public sealed record GetStageTasksByStageQuery(Guid StageId) : IQuery<IReadOnlyList<StageTaskResponse>>;

public sealed record StageSubTaskResponse(Guid Id, Guid StageTaskId, string Title, string? Description, int Order, bool IsRequired);

public sealed record CreateStageSubTaskCommand(Guid StageTaskId, string Title, int Order, bool IsRequired = true, string? Description = null)
    : ICommand<Guid>;

public sealed record UpdateStageSubTaskCommand(Guid StageSubTaskId, string? Title = null, int? Order = null, bool? IsRequired = null, string? Description = null)
    : ICommand;

public sealed record DeleteStageSubTaskCommand(Guid StageSubTaskId) : ICommand;

public sealed record GetStageSubTasksByStageTaskQuery(Guid StageTaskId) : IQuery<IReadOnlyList<StageSubTaskResponse>>;

/// <summary>One row per (template stage, department) — materialized into a <c>StageTeam</c> at
/// instantiation (G5-07), per the backend's <c>AddProjectTemplateAsync</c>.</summary>
public sealed record AddStageDepartmentCommand(Guid StageId, Guid DepartmentId) : ICommand;

public sealed record RemoveStageDepartmentCommand(Guid StageId, Guid DepartmentId) : ICommand;

public sealed record StageDepartmentResponse(Guid StageId, Guid DepartmentId);

public sealed record GetStageDepartmentsQuery(Guid StageId) : IQuery<IReadOnlyList<StageDepartmentResponse>>;
