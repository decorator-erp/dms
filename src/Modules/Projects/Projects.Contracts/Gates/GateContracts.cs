using BuildingBlocks.Messaging;

namespace Projects.Contracts.Gates;

/// <summary><c>ApprovalId</c> is populated once an <see cref="Projects.Contracts.ConditionType.Approval"/>
/// gate has requested its approval (G5-08) — nullable until then, and for every other condition
/// type.</summary>
public sealed record GateConditionResponse(
    Guid Id, Guid? ProjectStageId, Guid? ProjectTaskId, ConditionType ConditionType, GateTrigger Trigger,
    string Label, Guid? ReferencedTaskId, Guid? ReferencedStageId, ApprovalType? ApprovalType,
    GateStatus Status, Guid? ApprovalId,
    Guid? ReferencedDocumentId, Guid? ResolvedById, DateTime? ResolvedAt, string? ResolutionNote);

/// <summary><see cref="ReferencedDocumentId"/> added at G7-07 — a trailing optional positional parameter,
/// additive and source-compatible, mirroring <c>GateConditionContext</c>'s own G7-02 extension. Without
/// it a <see cref="Projects.Contracts.ConditionType.DocumentApproved"/> gate could never actually be
/// created through this command surface: <c>GateConditionContext</c>/<c>GateCondition.Create</c> both
/// already accepted the value, but nothing threaded a caller-supplied one in.</summary>
public sealed record CreateStageGateConditionCommand(
    Guid ProjectId, Guid StageId, string UserId, ConditionType ConditionType, GateTrigger Trigger, string Label,
    Guid? ReferencedTaskId = null, Guid? ReferencedStageId = null, ApprovalType? ApprovalType = null,
    Guid? ReferencedDocumentId = null)
    : ICommand<Guid>, IProjectScoped, IRequiresProjectManager;

/// <summary>Same <see cref="ReferencedDocumentId"/> addition as <see cref="CreateStageGateConditionCommand"/>,
/// for the same reason.</summary>
public sealed record CreateTaskGateConditionCommand(
    Guid ProjectId, Guid TaskId, string UserId, ConditionType ConditionType, GateTrigger Trigger, string Label,
    Guid? ReferencedTaskId = null, Guid? ReferencedStageId = null, ApprovalType? ApprovalType = null,
    Guid? ReferencedDocumentId = null)
    : ICommand<Guid>, IProjectScoped, IRequiresProjectManager;

public sealed record DeleteGateConditionCommand(Guid ProjectId, Guid GateConditionId, string UserId)
    : ICommand, IProjectScoped, IRequiresProjectManager;

/// <summary>Manual satisfaction — the <c>ManualCheckHandler</c> path (G5-03). Approval-type gates are
/// satisfied reactively by <c>ApprovalDecidedIntegrationEventHandler</c> (G5-08), never through this
/// command.</summary>
public sealed record SatisfyGateConditionCommand(Guid ProjectId, Guid GateConditionId, string UserId, string? Note = null)
    : ICommand, IProjectScoped, IRequiresProjectManager;

public sealed record GetStageGateConditionsQuery(Guid ProjectId, Guid StageId, string UserId)
    : IQuery<IReadOnlyList<GateConditionResponse>>, IProjectScoped;

public sealed record GetTaskGateConditionsQuery(Guid ProjectId, Guid TaskId, string UserId)
    : IQuery<IReadOnlyList<GateConditionResponse>>, IProjectScoped;
