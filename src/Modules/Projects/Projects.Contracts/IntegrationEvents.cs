using BuildingBlocks.Events;

namespace Projects.Contracts;

/// <summary><c>StageName</c> denormalized — a subscriber (Notifications, later) shouldn't need a second
/// call back into Projects just to render a message.</summary>
public sealed record ProjectStageCompletedIntegrationEvent(
    Guid EventId, Guid ProjectId, Guid StageId, string StageName, DateTimeOffset OccurredAtUtc)
    : IIntegrationEvent;

public sealed record ProjectStageRejectedIntegrationEvent(
    Guid EventId, Guid ProjectId, Guid StageId, string StageName, DateTimeOffset OccurredAtUtc)
    : IIntegrationEvent;

/// <summary><c>TaskName</c>/<c>AssigneeEmployeeId</c> denormalized on every task lifecycle event, same
/// reasoning as <c>StageName</c> above.</summary>
public sealed record ProjectTaskAssignedIntegrationEvent(
    Guid EventId, Guid ProjectId, Guid TaskId, string TaskName, Guid AssigneeEmployeeId,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

public sealed record ProjectTaskStartedIntegrationEvent(
    Guid EventId, Guid ProjectId, Guid TaskId, string TaskName, Guid? AssigneeEmployeeId,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

public sealed record ProjectTaskCompletedIntegrationEvent(
    Guid EventId, Guid ProjectId, Guid TaskId, string TaskName, Guid? AssigneeEmployeeId,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

public sealed record ProjectTaskRejectedIntegrationEvent(
    Guid EventId, Guid ProjectId, Guid TaskId, string TaskName, Guid? AssigneeEmployeeId,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

public sealed record ProjectTaskCancelledIntegrationEvent(
    Guid EventId, Guid ProjectId, Guid TaskId, string TaskName, Guid? AssigneeEmployeeId,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

public sealed record ProjectTaskReopenedIntegrationEvent(
    Guid EventId, Guid ProjectId, Guid TaskId, string TaskName, Guid? AssigneeEmployeeId,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

/// <summary>Published when a gate transitions to <see cref="GateStatus.Satisfied"/> — the cascade
/// (unlocking dependents) is intra-module (the G0-03 dispatcher re-loop already carries it); this event
/// is for OTHER modules that may care a gate resolved (Notifications, later). Zero subscribers today —
/// R2's zero-handler no-op applies.</summary>
public sealed record GateConditionSatisfiedIntegrationEvent(
    Guid EventId, Guid ProjectId, Guid GateConditionId, GateOwnerKind OwnerKind, Guid OwnerId,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
