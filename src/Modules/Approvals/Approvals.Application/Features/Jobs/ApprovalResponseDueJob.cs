using Approvals.Domain;
using Approvals.Infrastructure.Persistence;
using BuildingBlocks.Jobs;
using Microsoft.EntityFrameworkCore;
using Notifications.Contracts;

namespace Approvals.Application.Features.Jobs;

/// <summary>
/// Nudges eligible approvers when an approval passes <see cref="Approval.DueAtUtc"/> with no decision.
///
/// <para><b>Simpler than the backend's version, and correct where that one was not.</b> The backend
/// stores a single XOR'd approver as either <c>AssignedEmployeeId</c> or <c>AssignedClientId</c> and
/// resolves each to a user id per row — and its own source comments record that an earlier
/// <c>AssignedEmployeeId != null</c> filter silently excluded every client-assigned approval, so client
/// SLAs never fired at all. This module's <c>Approval</c> is a deliberate redesign (G4-01): approvers
/// are a SET of Identity user ids, employee-or-client resolved by the requesting module before the
/// approval is ever created. So there is no resolution step here, no XOR to get wrong, and no class of
/// approver that can be filtered out by accident — every eligible approver is notified because every
/// eligible approver is already a row in the set.</para>
///
/// <para>Consequently this job needs no cross-module contract at all: <c>Approvals.Application</c>
/// references only its own Contracts plus <c>Notifications.Contracts</c>.</para>
/// </summary>
internal sealed class ApprovalResponseDueJob(
    ApprovalsDbContext db,
    INotificationService notifications,
    TimeProvider timeProvider) : IPeriodicJob
{
    public string Name => nameof(ApprovalResponseDueJob);

    public async Task RunAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        var overdue = await db.Approvals
            .AsNoTracking()
            .Where(a => a.IsActive
                        && a.Decision == null
                        && a.DueAtUtc != null && a.DueAtUtc < now)
            .Select(a => new { a.Id, Approvers = a.Approvers.Select(p => p.ApproverUserId).ToList() })
            .ToListAsync(ct);

        foreach (var approval in overdue)
        {
            var targets = approval.Approvers
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (targets.Count == 0)
                continue;

            await notifications.NotifyManyAsync(
                targets,
                NotificationType.ApprovalResponseOverdue,
                title: "Approval response overdue",
                body: "An approval you can decide is past its response deadline.",
                entityType: NotificationEntityType.Approval,
                entityId: approval.Id,
                dedupeKey: $"approval-{approval.Id}-sla",
                ct: ct);
        }
    }
}
