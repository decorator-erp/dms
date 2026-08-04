using SharedKernel;

namespace Approvals.Domain;

/// <summary>
/// Raised by <see cref="Approval.Decide"/> on every decision (Approved/Rejected/ChangesRequested alike —
/// matching the backend's own <c>ApprovalDomainService.DecideAsync</c>, which enqueues its domain event
/// unconditionally and lets the subject-callback fan-out branch per decision afterward). Handled inside
/// <c>Approvals.Application</c> to publish <c>Approvals.Contracts.ApprovalDecidedIntegrationEvent</c> on
/// the outbox in the same transaction — the integration event is the cross-module-visible one; this
/// domain event never leaves the module.
/// </summary>
internal sealed record ApprovalDecidedDomainEvent(
    Guid ApprovalId, ApprovalSubjectType SubjectType, Guid SubjectId, ApprovalDecision Decision,
    string DecidedByUserId) : IDomainEvent;
