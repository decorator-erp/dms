using SharedKernel;

namespace Projects.Domain;

/// <summary>Raised by <see cref="GateCondition.MarkSatisfied"/> — consumed intra-module (G5-08) to
/// cascade-unlock the owner (<c>ProjectStage.Unlock</c>/<c>ProjectTask.Unlock</c> for
/// <c>GateTrigger.Unlock</c> gates, or to permit completion for <c>GateTrigger.Complete</c> gates). The
/// triggering handler also directly enqueues <c>Projects.Contracts.GateConditionSatisfiedIntegrationEvent</c>
/// in its own <c>SaveChangesAsync</c>, same atomicity reasoning as every other direct-enqueue decision in
/// this plan.</summary>
internal sealed record GateConditionSatisfiedDomainEvent(Guid GateConditionId, GateOwnerKind OwnerKind, Guid OwnerId) : IDomainEvent;
