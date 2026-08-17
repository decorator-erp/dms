using Approvals.Contracts;
using Approvals.Infrastructure.Persistence;
using BuildingBlocks.Events;
using Microsoft.EntityFrameworkCore;
using Notifications.Contracts;

namespace Approvals.Infrastructure.IntegrationEvents;

/// <summary>
/// <b>These live in Approvals, not in Notifications, because their recipients are rows in
/// <c>approvals.approvals</c>.</b>
///
/// <para>Neither event carries who should be told. <c>ApprovalRequestedIntegrationEvent</c> has the
/// approval id and its subject, not the eligible approvers;
/// <c>ApprovalDecidedIntegrationEvent</c> has who decided, not who asked. Both are in this module's own
/// table. The alternative — putting the approver list on the event payload, or adding a read contract
/// so a handler in Notifications could fetch it — would export this module's internals to serve one
/// caller. Moving the handler to the data costs a project reference.</para>
///
/// <para><b>Approvals is now a subscriber to its own published event.</b> That is legal (nothing
/// forbids it, and <c>EventBus</c> fans out by runtime type without regard to publisher) but it was
/// unprecedented in this repo before N1-06, so it is recorded rather than assumed. It is also why
/// <c>ApprovalsModule</c> finally calls <c>AddModuleIntegrationEvents</c> — its own doc comment
/// previously read "No AddModuleIntegrationEvents call — unlike HR, Approvals has no…"; a handler
/// placed here without that call would be registered nowhere, and the event would be marked processed
/// with zero handlers, silently.</para>
/// </summary>
internal sealed class ApprovalRequestedNotificationHandler(
    ApprovalsDbContext db,
    INotificationService notifications) : IIntegrationEventHandler<ApprovalRequestedIntegrationEvent>
{
    public async Task HandleAsync(ApprovalRequestedIntegrationEvent @event, CancellationToken ct)
    {
        var approvers = await db.Approvals
            .AsNoTracking()
            .Where(a => a.Id == @event.ApprovalId)
            .SelectMany(a => a.Approvers.Select(p => p.ApproverUserId))
            .ToListAsync(ct);

        var targets = approvers.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToList();
        if (targets.Count == 0)
            return;

        await notifications.NotifyManyAsync(
            targets,
            NotificationType.ApprovalRequested,
            title: "Approval requested",
            body: $"A {@event.SubjectType} is waiting for your decision.",
            entityType: NotificationEntityType.Approval,
            entityId: @event.ApprovalId,
            dedupeKey: $"approval-{@event.ApprovalId}-requested",
            ct: ct);
    }
}

/// <summary>
/// Tells the requester the outcome. Deliberately does NOT notify the other eligible approvers: they
/// were asked to decide, someone did, and "you no longer need to act" is not worth a badge — the
/// approval simply leaves their queue.
/// </summary>
internal sealed class ApprovalDecidedNotificationHandler(
    ApprovalsDbContext db,
    INotificationService notifications) : IIntegrationEventHandler<ApprovalDecidedIntegrationEvent>
{
    public async Task HandleAsync(ApprovalDecidedIntegrationEvent @event, CancellationToken ct)
    {
        var requestedByUserId = await db.Approvals
            .AsNoTracking()
            .Where(a => a.Id == @event.ApprovalId)
            .Select(a => a.RequestedByUserId)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(requestedByUserId))
            return;

        // The decider and the requester can be the same person when someone with DecideAny overrides
        // their own request; there is no point telling them what they just did.
        if (string.Equals(requestedByUserId, @event.DecidedByUserId, StringComparison.Ordinal))
            return;

        var approved = @event.Decision == ApprovalDecision.Approved;

        await notifications.NotifyAsync(
            requestedByUserId,
            approved ? NotificationType.ApprovalApproved : NotificationType.ApprovalRejected,
            title: approved ? "Approval granted" : $"Approval {@event.Decision}",
            body: $"Your {@event.SubjectType} approval request has been decided: {@event.Decision}.",
            entityType: NotificationEntityType.Approval,
            entityId: @event.ApprovalId,
            dedupeKey: $"approval-{@event.ApprovalId}-decided",
            ct: ct);
    }
}
