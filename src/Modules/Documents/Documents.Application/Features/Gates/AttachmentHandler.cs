using Projects.Contracts;
using SharedKernel;

namespace Documents.Application.Features.Gates;

/// <summary>
/// Ported as-is from the backend: a no-op at CREATE-time validation. The real check is reactive, at
/// SATISFY time — <c>Projects.Infrastructure</c>'s <c>DocumentUploadedIntegrationEvent</c> handler (this
/// task) is what actually satisfies an <c>Attachment</c> gate, by checking whether a document now exists
/// for the gate's owner (stage or task). Before this handler existed, <c>ConditionType.Attachment</c>
/// gates were rejected outright at creation with <c>GateConditionErrors.UnsupportedConditionType</c> —
/// this is the type that makes them creatable at all (structural item 2's whole point).
/// </summary>
internal sealed class AttachmentHandler : IGateConditionTypeHandler
{
    public ConditionType Type => ConditionType.Attachment;

    public Task<Result> ValidateAsync(GateConditionContext context, CancellationToken ct = default) =>
        Task.FromResult(Result.Success());
}
