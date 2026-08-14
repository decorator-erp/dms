namespace Crm.Contracts;

/// <summary>
/// Read-only helpers for the three CRM gate-condition evaluators (<c>CrmPeriodClosed</c>,
/// <c>CrmBriefApproved</c>, <c>CrmItemsResolved</c>), ported from the backend's
/// <c>ICrmGateReader</c> — minus its two document-lineage methods
/// (<c>GetDocumentLineageProjectAsync</c>/<c>IsLatestDocumentApprovedAsync</c>), which are Documents'
/// data bundled here only for the backend's convenience. Those stay out; Documents' own
/// <c>DocumentApprovedHandler</c> already reads its own <c>DocumentsDbContext</c> directly (Phase 3),
/// and the same "read your own module's data" rule applies here — there is no other-module case for
/// this interface to serve.
///
/// <para>Called from two places, per this phase's push-and-pull gate seam: automatically, by
/// <c>Projects.Infrastructure</c>'s subscribers to the three CRM satisfaction events (push); and
/// on-demand, by <c>SatisfyGateConditionCommandHandler</c> for a manual satisfy of a CRM-typed gate
/// (pull) — the case a satisfaction event can never retroactively cover: a gate created after its CRM
/// condition already became true.</para>
/// </summary>
public interface ICrmGateReader
{
    /// <summary>Some requirement period references the stage, and none of them are
    /// <see cref="RequirementPeriodStatus.Open"/>.</summary>
    Task<bool> HasClosedGatheringAsync(Guid projectStageId, CancellationToken ct);

    Task<bool> IsBriefApprovedForStageAsync(Guid projectStageId, CancellationToken ct);

    /// <summary>Unresolved = brief spaces / requirement items on this stage's brief / period(s) whose
    /// approval status is <see cref="CrmApprovalState.Draft"/> or
    /// <see cref="CrmApprovalState.PendingApproval"/>.</summary>
    Task<int> CountUnresolvedForStageAsync(Guid projectStageId, CancellationToken ct);

    /// <summary>Same, but scoped to a single project task's own entries/items — the only CRM gate that
    /// can be task-owned.</summary>
    Task<int> CountUnresolvedForTaskAsync(Guid projectTaskId, CancellationToken ct);
}
