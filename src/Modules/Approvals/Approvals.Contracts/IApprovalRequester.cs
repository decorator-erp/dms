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

public sealed record ApprovalRequest(
    ApprovalSubjectType SubjectType, Guid SubjectId, ApprovalType Type,
    string RequestedByUserId, IReadOnlyList<ApproverSpec> Approvers, DateTimeOffset? DueAtUtc);

public sealed record ApprovalSnapshot(Guid Id, ApprovalSubjectType SubjectType, Guid SubjectId,
    ApprovalState State, ApprovalDecision? Decision, DateTimeOffset? DecidedAtUtc);

/// <summary>
/// The only way another module creates or reads an approval. Approvals never reads another module's
/// <c>*.Contracts</c> — every parameter here is a bare id or primitive, nothing is validated against
/// another module's data.
/// </summary>
public interface IApprovalRequester
{
    /// <summary>Creates a pending approval for a subject owned by the calling module. Returns the approval id.</summary>
    Task<Result<Guid>> RequestAsync(ApprovalRequest request, CancellationToken ct);
    Task<ApprovalSnapshot?> GetAsync(Guid approvalId, CancellationToken ct);
    Task<IReadOnlyList<ApprovalSnapshot>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
