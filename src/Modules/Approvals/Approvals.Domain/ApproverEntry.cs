namespace Approvals.Domain;

/// <summary>
/// One eligible decider for an <see cref="Approval"/>. Mirrors
/// <c>Approvals.Contracts.ApproverSpec</c>; <see cref="ApproverUserId"/> is an Identity
/// <c>ApplicationUser</c> id (string), never an Employee <c>Guid</c> — see <see cref="Approval"/>'s own
/// remarks. Owned, no identity of its own (same shape as the backend's owned <c>ChangeItem</c> on
/// <c>Approval</c>) — no FK, no separate table row lifecycle.
/// </summary>
internal sealed record ApproverEntry(ApproverKind Kind, string ApproverUserId);
