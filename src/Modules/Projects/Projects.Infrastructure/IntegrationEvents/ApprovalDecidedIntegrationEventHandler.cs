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
/// its own <c>SaveChangesAsync</c>.
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
            return;
        }

        Guid? employeeId = await hrDirectory.ResolveEmployeeIdAsync(@event.DecidedByUserId, ct);

        switch (@event.Decision)
        {
            case ApprovalDecision.Approved:
                gate.MarkSatisfied(employeeId);
                break;
            case ApprovalDecision.Rejected:
                gate.MarkRejected(employeeId);
                // B-P-H4: a rejected Unlock gate can never be satisfied, so its owner is released once no other
                // Unlock gate of it is pending, instead of staying Locked for good.
                await GateOwnerLocks.ReleaseIfLastAsync(db, gate, ct);
                break;
            case ApprovalDecision.ChangesRequested:
                break;
        }
    }
}
