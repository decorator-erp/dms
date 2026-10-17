using Approvals.Domain;
using Approvals.Infrastructure.Persistence;
using BuildingBlocks.Jobs;
using Microsoft.EntityFrameworkCore;
using Notifications.Contracts;

namespace Approvals.Application.Features.Jobs;

/// <summary>
/// After a grace window past the response deadline, tells the person who ASKED for the decision that it
/// is still stuck. Never mutates <see cref="Approval.Decision"/> — auto-approving or auto-rejecting
/// would take the decision away from a human, which this module deliberately avoids. Idempotent via its
/// own dedupe key, distinct from <c>ApprovalResponseDueJob</c>'s, so the two notifications do not
/// suppress each other.
///
/// <para><b>Deliberate divergence: the recipient is the requester, not the project owner [N7].</b> The
/// backend escalates to the project owner, reached via <c>Approval.ProjectId</c>. This module's
/// <c>Approval</c> has no <c>ProjectId</c> and cannot have one — G4-01 collapsed the backend's five
/// subject FKs into a <c>SubjectType</c>/<c>SubjectId</c> pair precisely because Approvals never
/// navigates to the subject, which lives in another module's schema. Reading
/// <c>projects.projects</c> from here would break the schema isolation the module was designed around,
/// and adding a project-owner lookup to <c>IProjectDirectory</c> would extend another module's
/// Contracts for one caller.</para>
///
/// <para>The requester is the honest target available in this module's own data — and arguably the
/// better one: they are the person blocked by the missing decision, and the one who can chase it or
/// re-route it. What is genuinely lost is escalation ABOVE the approver to someone with authority over
/// them; Approvals has no notion of that hierarchy, and inventing one here would be a design change
/// rather than a port. Recorded rather than quietly narrowed.</para>
/// </summary>
internal sealed class ApprovalOverdueEscalationJob(
    ApprovalsDbContext db,
    INotificationService notifications,
    TimeProvider timeProvider) : IPeriodicJob
{
    /// <summary>Grace after the response deadline before telling the requester it is stuck.</summary>
    private const int GraceDays = 2;

    public string Name => nameof(ApprovalOverdueEscalationJob);

    public async Task RunAsync(CancellationToken ct)
    {
        DateTimeOffset cutoff = timeProvider.GetUtcNow().AddDays(-GraceDays);

        var stuck = await db.Approvals
            .AsNoTracking()
            .Where(a => a.IsActive
                        && a.Decision == null
                        && a.DueAtUtc != null && a.DueAtUtc < cutoff)
            .Select(a => new { a.Id, a.RequestedByUserId })
            .ToListAsync(ct);

        foreach (var approval in stuck)
        {
            if (string.IsNullOrEmpty(approval.RequestedByUserId))
            {
                continue;
            }

            await notifications.NotifyAsync(
                approval.RequestedByUserId,
                NotificationType.ApprovalEscalated,
                title: "Approval overdue — still undecided",
                body: "An approval you requested is past its response deadline beyond the grace period "
                      + "and has had no decision.",
                entityType: NotificationEntityType.Approval,
                entityId: approval.Id,
                dedupeKey: $"approval-{approval.Id}-escalated",
                ct: ct);
        }
    }
}
