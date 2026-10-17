using BuildingBlocks.Events;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Notifications.Contracts;
using Sites.Contracts;
using Sites.Infrastructure.Persistence;

namespace Sites.Infrastructure.IntegrationEvents;

/// <summary>
/// Tells the surveyor who submitted a site what the office decided.
///
/// <para><b>This lives in Sites because the payload carries the wrong person.</b>
/// <c>SiteApprovedIntegrationEvent</c> has <c>DecidedById</c> — the approver — and the recipient we
/// want is the SUBMITTER, which is <c>sites.sites.CreatedById</c>. A handler in Notifications would
/// need a site read contract to get it; the placement rule says the handler moves instead.</para>
///
/// <para>P5F-02 made this notification matter more than it looks. Submission now FREEZES a survey's
/// records, and reopening after a rejection is what unfreezes them — so a surveyor whose site was
/// rejected and who is not told has a form they cannot edit and no stated reason. The decision note is
/// carried in the body for exactly that.</para>
///
/// <para><c>SiteSubmittedIntegrationEvent</c> gets no handler, deliberately: Sites models no reviewer
/// role and no assignment, so there is no one to tell. That is a recorded decision, not an omission —
/// see PROGRESS.md's N1-06 table.</para>
/// </summary>
internal sealed class SiteApprovedNotificationHandler(
    SitesDbContext db,
    IHrDirectory hrDirectory,
    INotificationService notifications) : IIntegrationEventHandler<SiteApprovedIntegrationEvent>
{
    public async Task HandleAsync(SiteApprovedIntegrationEvent @event, CancellationToken ct)
    {
        var site = await db.Sites
            .AsNoTracking()
            .Where(s => s.Id == @event.SiteId)
            .Select(s => new { s.Code, s.CreatedById, s.DecisionNote })
            .FirstOrDefaultAsync(ct);

        if (site is null)
        {
            return;
        }

        EmployeeSummary? submitter = await hrDirectory.GetAsync(site.CreatedById, ct);
        if (string.IsNullOrEmpty(submitter?.AppUserId))
        {
            return;
        }

        string note = string.IsNullOrWhiteSpace(site.DecisionNote) ? string.Empty : $" Note: {site.DecisionNote}";

        await notifications.NotifyAsync(
            submitter.AppUserId,
            @event.Approved ? NotificationType.StageCompleted : NotificationType.StageRejected,
            title: @event.Approved ? $"Site approved — {site.Code}" : $"Site rejected — {site.Code}",
            body: @event.Approved
                ? $"Your survey '{site.Code}' has been approved.{note}"
                : $"Your survey '{site.Code}' was rejected and has been reopened for rework.{note}",
            entityType: NotificationEntityType.Site,
            entityId: @event.SiteId,
            dedupeKey: $"site-{@event.SiteId}-decided-{@event.EventId}",
            ct: ct);
    }
}
