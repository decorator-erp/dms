using BuildingBlocks.Messaging;

namespace Projects.Contracts.Scope;

/// <summary>One picked template stage. <see cref="SourceTemplateName"/> is read live from the template and is
/// null once that stage has been deleted; <see cref="Name"/>, <see cref="Description"/> and
/// <see cref="Fee"/> are the scope's own copies. <see cref="CreatedStageId"/> is set by confirm.</summary>
public sealed record ScopeOfWorkItemResponse(
    Guid Id, Guid SourceStageId, string? SourceTemplateName, string Name, string? Description, int Order, decimal Fee,
    Guid? CreatedStageId);

/// <summary>The project's scope of work (نطاق العمل). <see cref="Id"/> is null while none has been saved:
/// the read then returns an empty Draft without creating a row. <see cref="Subtotal"/> is the sum of the fees,
/// <see cref="Total"/> adds <see cref="AdjustmentAmount"/> (negative is a discount), and
/// <see cref="EstimatedEndDate"/> is <see cref="PlannedStartDate"/> + <see cref="DurationDays"/>.</summary>
public sealed record ScopeOfWorkResponse(
    Guid? Id, Guid ProjectId, ScopeOfWorkStatus Status, int? DurationDays, DateOnly? PlannedStartDate,
    DateOnly? EstimatedEndDate, decimal Subtotal, decimal AdjustmentAmount, string? AdjustmentNote, decimal Total,
    string? Notes, DateTime? ConfirmedAtUtc, Guid? ConfirmedById, IReadOnlyList<ScopeOfWorkItemResponse> Items);

public sealed record GetScopeOfWorkQuery(Guid ProjectId, string UserId) : IQuery<ScopeOfWorkResponse>, IProjectScoped;

/// <summary>Replaces the whole header; creates the Draft on the project's first save.</summary>
public sealed record UpdateScopeOfWorkHeaderCommand(
    Guid ProjectId, string UserId, int? DurationDays, DateOnly? PlannedStartDate, decimal AdjustmentAmount,
    string? AdjustmentNote, string? Notes) : ICommand, IProjectScoped;

/// <summary>Appends template stages, in the order given, copying each one's name, description and default
/// fee (0 when it has none). Returns the added items.</summary>
public sealed record AddScopeOfWorkItemsCommand(Guid ProjectId, string UserId, IReadOnlyList<Guid> SourceStageIds)
    : ICommand<IReadOnlyList<ScopeOfWorkItemResponse>>, IProjectScoped;

/// <summary>Appends every stage of a template, in template order, skipping stages already picked. Returns the
/// added items (none when every stage was already picked).</summary>
public sealed record AddScopeOfWorkTemplateCommand(Guid ProjectId, string UserId, Guid StageTemplateId)
    : ICommand<IReadOnlyList<ScopeOfWorkItemResponse>>, IProjectScoped;

public sealed record UpdateScopeOfWorkItemCommand(
    Guid ProjectId, string UserId, Guid ItemId, string Name, string? Description, decimal Fee) : ICommand, IProjectScoped;

/// <summary>Removes one item; the rest are renumbered 1..n.</summary>
public sealed record RemoveScopeOfWorkItemCommand(Guid ProjectId, string UserId, Guid ItemId) : ICommand, IProjectScoped;

/// <summary><see cref="ItemIds"/> is the full new order: exactly the current items, each once.</summary>
public sealed record ReorderScopeOfWorkItemsCommand(Guid ProjectId, string UserId, IReadOnlyList<Guid> ItemIds)
    : ICommand, IProjectScoped;
