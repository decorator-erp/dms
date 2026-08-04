using BuildingBlocks.Events;

namespace Approvals.Contracts;

/// <summary>
/// Published alongside approval creation. No subscriber exists yet in this phase (R2: zero-handler
/// events are a silent, successful no-op) — kept because <c>IApprovalRequester.RequestAsync</c> being
/// synchronous doesn't preclude other modules wanting to react to a NEW approval later (e.g. a
/// notifications module), same reasoning as the backend's own <c>ApprovalRequestedDomainEvent</c>.
/// </summary>
public sealed record ApprovalRequestedIntegrationEvent(
    Guid EventId, Guid ApprovalId, ApprovalSubjectType SubjectType, Guid SubjectId,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

/// <summary>
/// Published in the same <c>SaveChangesAsync</c> as the deciding write. Consumed by
/// <c>Projects.Infrastructure</c>'s <c>ApprovalDecidedIntegrationEventHandler</c> (G5-08), filtered to
/// <see cref="ApprovalSubjectType.GateCondition"/> — other subject types belong to modules that don't
/// exist yet and are left unhandled, marked processed via the zero-*matching*-handler path.
/// </summary>
public sealed record ApprovalDecidedIntegrationEvent(
    Guid EventId, Guid ApprovalId, ApprovalSubjectType SubjectType, Guid SubjectId,
    ApprovalDecision Decision, string DecidedByUserId, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
