using Crm.Domain;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Gates;

/// <summary>
/// CrmBriefApproved — the stage's design brief head is Approved by the client. Satisfaction is
/// one-way: a later head edit re-opens approval on the head, but a satisfied gate stays satisfied.
/// Stage-owner-only, matching the backend's own <c>CrmBriefApprovedHandler</c> — verified against the
/// backend before this task started (Lesson 1, <c>PLAN-PHASE4.md</c>): validation reads only
/// <see cref="GateConditionContext.OwnerKind"/>, so no <c>GateConditionContext</c> extension and no new
/// <c>GateCondition</c> column were needed for this gate type.
/// </summary>
internal sealed class CrmBriefApprovedHandler : IGateConditionTypeHandler
{
    public ConditionType Type => ConditionType.CrmBriefApproved;

    public Task<Result> ValidateAsync(GateConditionContext context, CancellationToken ct = default) =>
        Task.FromResult(context.OwnerKind == GateOwnerKind.Stage
            ? Result.Success()
            : Result.Failure(CrmGateErrors.UnsupportedOwner));
}

/// <summary>
/// CrmItemsResolved — no unresolved gathering entries / requirement items remain in the owner's
/// scope. Stage-owned gates count everything on the stage's brief/period(s); task-owned gates count
/// only entries/items pinned to the task. The only CRM gate that can live on a task — both owner
/// kinds are valid, so this handler never fails shape validation.
/// </summary>
internal sealed class CrmItemsResolvedHandler : IGateConditionTypeHandler
{
    public ConditionType Type => ConditionType.CrmItemsResolved;

    public Task<Result> ValidateAsync(GateConditionContext context, CancellationToken ct = default) =>
        Task.FromResult(Result.Success());
}

/// <summary>
/// CrmPeriodClosed — the requirement-gathering window for the stage has closed. Stage-owner only;
/// satisfaction is verified against real CRM data at satisfy time via <c>ICrmGateReader</c> (the pull
/// half of this phase's push-and-pull gate seam), never trusted from the gate's own creation-time
/// validation alone.
/// </summary>
internal sealed class CrmPeriodClosedHandler : IGateConditionTypeHandler
{
    public ConditionType Type => ConditionType.CrmPeriodClosed;

    public Task<Result> ValidateAsync(GateConditionContext context, CancellationToken ct = default) =>
        Task.FromResult(context.OwnerKind == GateOwnerKind.Stage
            ? Result.Success()
            : Result.Failure(CrmGateErrors.UnsupportedOwner));
}
