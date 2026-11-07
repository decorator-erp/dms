using BuildingBlocks.Events;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
/// carried in the body for exactly that — from the event payload, since a reopen clears the row's copy
/// (NS4).</para>
///
/// <para><c>SiteSubmittedIntegrationEvent</c> gets no handler, deliberately: Sites models no reviewer
/// role and no assignment, so there is no one to tell. That is a recorded decision, not an omission —
/// see PROGRESS.md's N1-06 table.</para>
/// </summary>
internal sealed partial class SiteApprovedNotificationHandler(
    SitesDbContext db,
    IHrDirectory hrDirectory,
    INotificationService notifications,
    ILogger<SiteApprovedNotificationHandler> logger) : IIntegrationEventHandler<SiteApprovedIntegrationEvent>
{
    public async Task HandleAsync(SiteApprovedIntegrationEvent @event, CancellationToken ct)
    {
        var site = await db.Sites
            .AsNoTracking()
            .Where(s => s.Id == @event.SiteId)
            .Select(s => new { s.Code, s.CreatedById })
            .FirstOrDefaultAsync(ct);

        if (site is null)
        {
            // B-S-L3: a decided site that is gone (soft-deleted, or a stray event) is worth a trace --
            // the submitter is never told, and nothing else would record why.
            LogSiteMissing(logger, @event.SiteId, @event.EventId);
            return;
        }

        EmployeeSummary? submitter = await hrDirectory.GetAsync(site.CreatedById, ct);
        if (string.IsNullOrEmpty(submitter?.AppUserId))
        {
            return;
        }

        // NS4: the note comes from the payload. The row's copy is cleared by a reopen, which can land
        // before this event is delivered.
        string note = string.IsNullOrWhiteSpace(@event.DecisionNote) ? string.Empty : $" Note: {@event.DecisionNote}";

        await notifications.NotifyAsync(
            submitter.AppUserId,
            @event.Approved ? NotificationType.SiteApproved : NotificationType.SiteRejected,
            title: @event.Approved ? $"Site approved — {site.Code}" : $"Site rejected — {site.Code}",
            body: @event.Approved
                ? $"Your survey '{site.Code}' has been approved.{note}"
                // S11: a rejection does NOT reopen the survey -- it stays Rejected until it is reopened.
                : $"Your survey '{site.Code}' was rejected. Reopen it for rework to make changes.{note}",
            entityType: NotificationEntityType.Site,
            entityId: @event.SiteId,
            dedupeKey: $"site-{@event.SiteId}-decided-{@event.EventId}",
            ct: ct);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Site {SiteId} was not found while delivering decision event {EventId}; no notification sent.")]
    private static partial void LogSiteMissing(ILogger logger, Guid siteId, Guid eventId);
}
