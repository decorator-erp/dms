using BuildingBlocks.Messaging;

namespace Projects.Contracts.Gates;

/// <summary><c>ApprovalId</c> is populated once an <see cref="Projects.Contracts.ConditionType.Approval"/>
/// gate has requested its approval (G5-08) — nullable until then, and for every other condition
/// type.</summary>
public sealed record GateConditionResponse(
    Guid Id, Guid? ProjectStageId, Guid? ProjectTaskId, ConditionType ConditionType, GateTrigger Trigger,
    string Label, Guid? ReferencedTaskId, Guid? ReferencedStageId, ApprovalType? ApprovalType,
    GateStatus Status, Guid? ApprovalId);

public sealed record CreateStageGateConditionCommand(
    Guid ProjectId, Guid StageId, string UserId, ConditionType ConditionType, GateTrigger Trigger, string Label,
    Guid? ReferencedTaskId = null, Guid? ReferencedStageId = null, ApprovalType? ApprovalType = null)
    : ICommand<Guid>, IProjectScoped;

public sealed record CreateTaskGateConditionCommand(
    Guid ProjectId, Guid TaskId, string UserId, ConditionType ConditionType, GateTrigger Trigger, string Label,
    Guid? ReferencedTaskId = null, Guid? ReferencedStageId = null, ApprovalType? ApprovalType = null)
    : ICommand<Guid>, IProjectScoped;

public sealed record DeleteGateConditionCommand(Guid ProjectId, Guid GateConditionId, string UserId)
    : ICommand, IProjectScoped;

/// <summary>Manual satisfaction — the <c>ManualCheckHandler</c> path (G5-03). Approval-type gates are
/// satisfied reactively by <c>ApprovalDecidedIntegrationEventHandler</c> (G5-08), never through this
/// command.</summary>
public sealed record SatisfyGateConditionCommand(Guid ProjectId, Guid GateConditionId, string UserId, string? Note = null)
    : ICommand, IProjectScoped;

public sealed record GetStageGateConditionsQuery(Guid ProjectId, Guid StageId, string UserId)
    : IQuery<IReadOnlyList<GateConditionResponse>>, IProjectScoped;

public sealed record GetTaskGateConditionsQuery(Guid ProjectId, Guid TaskId, string UserId)
    : IQuery<IReadOnlyList<GateConditionResponse>>, IProjectScoped;
