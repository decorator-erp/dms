using SharedKernel;

namespace Approvals.Contracts;

/// <summary>
/// One eligible decider. <see cref="ApproverUserId"/> is an Identity <c>ApplicationUser</c> id (a
/// string), never an Employee <c>Guid</c> — Approvals is a sink module with zero knowledge of HR's
/// schema; the calling module resolves Employee → AppUserId itself (e.g. via
/// <c>Hr.Contracts.IHrDirectory</c>) before calling <see cref="IApprovalRequester.RequestAsync"/>.
/// A request may name more than one eligible approver (e.g. a project's owner AND its stage-team head);
/// the first of them to decide, decides — this is a deliberate generalization of the backend's single
/// assigned-approver-per-row model (<c>Approval.AssignedEmployeeId</c>/<c>AssignedClientId</c>, exactly
/// one set), not a port of it. No formal quorum.
/// </summary>
public sealed record ApproverSpec(ApproverKind Kind, string ApproverUserId);

/// <summary><see cref="ApprovalId"/> is supplied by the caller, which makes
/// <see cref="IApprovalRequester.RequestAsync"/> idempotent per id: a caller whose own commit failed after
/// the approval committed can retry with the same id and get the same approval back, instead of an
/// orphan that blocks every later request as <c>OpenApprovalExists</c>. Callers with no retry story pass
/// <see cref="Guid.NewGuid"/>.</summary>
public sealed record ApprovalRequest(
    Guid ApprovalId, ApprovalSubjectType SubjectType, Guid SubjectId, ApprovalType Type,
    string RequestedByUserId, IReadOnlyList<ApproverSpec> Approvers, DateTimeOffset? DueAtUtc);

/// <summary>What <see cref="IApprovalRequester.RequestAsync"/> found or created. <see cref="Decision"/> is
/// non-null only when the id already existed for the same subject AND was already decided — the caller
/// missed that decision (its own row never committed, so the decided event found nothing to bind to) and
/// must apply it itself.</summary>
public sealed record ApprovalRequestOutcome(Guid ApprovalId, ApprovalDecision? Decision);

public sealed record ApprovalSnapshot(Guid Id, ApprovalSubjectType SubjectType, Guid SubjectId,
    ApprovalState State, ApprovalDecision? Decision, DateTimeOffset? DecidedAtUtc);

/// <summary>
/// The only way another module creates or reads an approval. Approvals never reads another module's
/// <c>*.Contracts</c> — every parameter here is a bare id or primitive, nothing is validated against
/// another module's data.
/// </summary>
public interface IApprovalRequester
{
    /// <summary>Creates a pending approval for a subject owned by the calling module, under the caller's
    /// <see cref="ApprovalRequest.ApprovalId"/>. Idempotent per id: an existing approval with that id for
    /// the same subject is returned as-is (with its decision, if any); for a different subject it fails
    /// with <c>approvals.ApprovalIdInUse</c>.</summary>
    Task<Result<ApprovalRequestOutcome>> RequestAsync(ApprovalRequest request, CancellationToken ct);
    Task<ApprovalSnapshot?> GetAsync(Guid approvalId, CancellationToken ct);
    Task<IReadOnlyList<ApprovalSnapshot>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>The subject's newest approval (by creation time), decided or not; null when it has none.
    /// For a caller whose own row lost the id it was sent under (CRM's legacy pending rows, D-10).</summary>
    Task<ApprovalSnapshot?> GetLatestForSubjectAsync(ApprovalSubjectType subjectType, Guid subjectId, CancellationToken ct);

    /// <summary>The many form of <see cref="GetLatestForSubjectAsync"/>: one snapshot per subject that has
    /// any approval; subjects with none are absent.</summary>
    Task<IReadOnlyList<ApprovalSnapshot>> GetLatestForSubjectsAsync(
        ApprovalSubjectType subjectType, IReadOnlyCollection<Guid> subjectIds, CancellationToken ct);
}
