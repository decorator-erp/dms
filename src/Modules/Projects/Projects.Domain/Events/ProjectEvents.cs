using SharedKernel;

namespace Projects.Domain;

/// <summary>
/// Raised by the entities themselves (<see cref="ProjectStage"/>/<see cref="ProjectTask"/>), consumed two
/// ways: (1) intra-module, by gate-resolution handlers in <c>Projects.Application</c> (G5-08) that react
/// to a stage/task transition by re-evaluating dependent <c>GateCondition</c>s — the cascade this drives
/// stays in-transaction via the command-path dispatcher; (2) the triggering command handler ALSO directly
/// enqueues the corresponding <c>Projects.Contracts</c> integration event in its own <c>SaveChangesAsync</c>
/// — same direct-enqueue-not-domain-event-indirection shape as Approvals' G4-04 decision, for the same
/// atomicity reason (a domain-event handler staging an outbox write would land in a second, separate
/// save).
/// </summary>
internal sealed record ProjectStageCompletedDomainEvent(Guid StageId, Guid ProjectId, string StageName) : IDomainEvent;

internal sealed record ProjectStageRejectedDomainEvent(Guid StageId, Guid ProjectId, string StageName) : IDomainEvent;

internal sealed record ProjectTaskAssignedDomainEvent(Guid TaskId, Guid ProjectId, string TaskName, Guid AssigneeEmployeeId) : IDomainEvent;

internal sealed record ProjectTaskStartedDomainEvent(Guid TaskId, Guid ProjectId, string TaskName, Guid? AssigneeEmployeeId) : IDomainEvent;

internal sealed record ProjectTaskCompletedDomainEvent(Guid TaskId, Guid ProjectId, string TaskName, Guid? AssigneeEmployeeId) : IDomainEvent;

internal sealed record ProjectTaskRejectedDomainEvent(Guid TaskId, Guid ProjectId, string TaskName, Guid? AssigneeEmployeeId) : IDomainEvent;

internal sealed record ProjectTaskCancelledDomainEvent(Guid TaskId, Guid ProjectId, string TaskName, Guid? AssigneeEmployeeId) : IDomainEvent;

internal sealed record ProjectTaskReopenedDomainEvent(Guid TaskId, Guid ProjectId, string TaskName, Guid? AssigneeEmployeeId) : IDomainEvent;
