using Approvals.Contracts;
using BuildingBlocks.Events;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Domain;
using Projects.Infrastructure.Persistence;

namespace Projects.Infrastructure.IntegrationEvents;

/// <summary>
/// Consumer side of decision #1's approval-gate flow (G5-08). Filtered to
/// <see cref="ApprovalSubjectType.GateCondition"/> — other subject types (DesignBrief, RequirementItem,
/// BriefSpace, ProjectDocument) belong to modules that don't exist yet and are left unhandled, marked
/// processed via the zero-*matching*-handler path (R2). <see cref="ApprovalDecision.ChangesRequested"/>
/// makes NO gate call at all (decision #4: the gate stays Pending, matching Approvals' own non-support
/// for a rework loop). Stages the write only — per <c>InboxDedupeDecorator</c>'s contract, never calls
/// its own <c>SaveChangesAsync</c>. A decided approval whose gate is not there yet throws, so the outbox retries it
/// (R1-6).
/// </summary>
internal sealed class ApprovalDecidedIntegrationEventHandler(ProjectsDbContext db, IHrDirectory hrDirectory)
    : IIntegrationEventHandler<ApprovalDecidedIntegrationEvent>
{
    public async Task HandleAsync(ApprovalDecidedIntegrationEvent @event, CancellationToken ct)
    {
        if (@event.SubjectType != ApprovalSubjectType.GateCondition)
        {
            return;
        }

        GateCondition? gate = await db.GateConditions.FirstOrDefaultAsync(g => g.Id == @event.SubjectId, ct);
        if (gate is null)
        {
            // R1-6 (B-P-M7's race): Approvals commits the request before the gate's own transaction does, so a
            // decision can arrive while the gate row is not visible yet. When the approval is one this module asked
            // for (its id is derived from the gate id, GateApprovalIds), a missing gate means "not committed yet" (a
            // pending approval gate can't be deleted: P4, WorkDeletionGuard): fail, and the outbox keeps the message
            // and retries it with backoff. The retries are bounded (Outbox:MaxAttempts, default 15, about 8 hours); a
            // gate whose save never committed leaves its approval's decision dead-lettered, and logged.
            if (@event.Decision == ApprovalDecision.ChangesRequested || @event.ApprovalId != GateApprovalIds.For(@event.SubjectId))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Gate condition {@event.SubjectId} for approval {@event.ApprovalId} is not visible yet; the decision will be retried.");
        }

        Guid? employeeId = await hrDirectory.ResolveEmployeeIdAsync(@event.DecidedByUserId, ct);

        switch (@event.Decision)
        {
            case ApprovalDecision.Approved:
                gate.MarkSatisfied(employeeId);
                break;
            case ApprovalDecision.Rejected:
                gate.MarkRejected(employeeId);
                // R1-4: a rejected approval keeps its owner Locked; GateOwnerLocks releases on rejection only for a
                // Finance (Budget) gate.
                await GateOwnerLocks.ReleaseOnRejectionAsync(db, gate, ct);
                break;
            case ApprovalDecision.ChangesRequested:
                break;
        }
    }
}
