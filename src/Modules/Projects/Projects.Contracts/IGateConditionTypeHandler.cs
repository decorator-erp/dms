using SharedKernel;

namespace Projects.Contracts;

/// <summary>
/// Ported from the backend's <c>GateConditionInput</c>. Create-time validation input only — satisfaction
/// is a separate concern (<c>GateConditionDomainService.SatisfyAsync</c>-equivalent logic, G5-08), not
/// delegated back to the handler.
///
/// <para>
/// <see cref="ReferencedDocumentId"/> added at G7-02 — the first time a module outside Projects
/// contributes a gate condition type (Documents' <c>AttachmentHandler</c>/<c>DocumentApprovedHandler</c>,
/// G7-07). A trailing optional positional parameter: additive and source-compatible with every existing
/// caller using named or positional args in order, exactly as the backend's own
/// <c>GateConditionInput</c> already carries it. The general rule this establishes, recorded in
/// <c>docs/adding-a-module.md</c>: a module contributing a gate type may need to extend this record;
/// that is expected and additive-only. Removing or repurposing an existing member is not.
/// </para>
/// </summary>
public sealed record GateConditionContext(
    Guid OwnerId, Guid OwnerProjectId, GateOwnerKind OwnerKind,
    Guid? ReferencedTaskId, Guid? ReferencedStageId, ApprovalType? ApprovalType,
    Guid? ReferencedDocumentId = null);

/// <summary>
/// Strategy for a single <see cref="ConditionType"/> — implemented by Projects itself today
/// (<c>ManualCheckHandler</c>, <c>StageReferenceHandler</c>, <c>TaskReferenceHandler</c>,
/// <c>ApprovalHandler</c>, G5-03/G5-08), by Finance/Documents/CRM later for the condition types this
/// phase rejects at creation. Shipping this in <c>Projects.Contracts</c> now — rather than keeping it
/// Projects-internal until another module actually needs it — is the design inversion the whole gate
/// system aims at: adding a new condition type is "add one handler + one registration," never a switch
/// statement to modify.
/// </summary>
public interface IGateConditionTypeHandler
{
    ConditionType Type { get; }
    Task<Result> ValidateAsync(GateConditionContext context, CancellationToken ct = default);
}
