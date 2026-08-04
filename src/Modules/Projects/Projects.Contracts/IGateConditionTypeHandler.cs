using SharedKernel;

namespace Projects.Contracts;

/// <summary>
/// Ported from the backend's <c>GateConditionInput</c>. Create-time validation input only — satisfaction
/// is a separate concern (<c>GateConditionDomainService.SatisfyAsync</c>-equivalent logic, G5-08), not
/// delegated back to the handler.
/// </summary>
public sealed record GateConditionContext(
    Guid OwnerId, Guid OwnerProjectId, GateOwnerKind OwnerKind,
    Guid? ReferencedTaskId, Guid? ReferencedStageId, ApprovalType? ApprovalType);

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
