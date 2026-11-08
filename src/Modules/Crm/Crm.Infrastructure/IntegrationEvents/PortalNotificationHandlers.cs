using BuildingBlocks.Events;
using Crm.Contracts;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Notifications.Contracts;
using Projects.Contracts;

namespace Crm.Infrastructure.IntegrationEvents;

/// <summary>
/// P2-A: tells the project's client that the office shared an item with them. The current state wins: a share that
/// was unshared meanwhile, or whose section is off (the client could not open it), notifies no one.
///
/// <para><c>INotificationService</c> commits in its own transaction (CLAUDE.md's recorded exception), so a redelivery
/// after that commit and before this handler's inbox row would notify again — hence the per-share dedupe key.</para>
/// </summary>
internal sealed class CrmPortalItemSharedNotificationHandler(
    CrmDbContext db, IProjectDirectory projects, INotificationService notifications)
    : IIntegrationEventHandler<CrmPortalItemSharedIntegrationEvent>
{
    public async Task HandleAsync(CrmPortalItemSharedIntegrationEvent @event, CancellationToken ct)
    {
        CrmPortalShare? share = await db.CrmPortalShares.AsNoTracking().FirstOrDefaultAsync(s => s.Id == @event.ShareId, ct);
        CrmPortalSettings? settings = await db.CrmPortalSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProjectId == @event.ProjectId, ct);
        if (share is null || !CrmPortalVisibility.CanSeeShare(settings, share))
        {
            return;
        }

        PortalClientRecipient? recipient = await PortalClientRecipient.ResolveAsync(db, projects, @event.ProjectId, ct);
        if (recipient is null)
        {
            return;
        }

        await notifications.NotifyAsync(
            recipient.AppUserId,
            NotificationType.CrmPortalItemShared,
            title: $"New in your portal: {recipient.ProjectName}",
            body: share.ClientTitle,
            entityType: NotificationEntityType.Project,
            entityId: @event.ProjectId,
            dedupeKey: $"crm-portal-share-{share.Id}",
            projectId: @event.ProjectId,
            ct: ct);
    }
}

/// <summary>P2-A: tells the project's client about a published update — unless it was deleted meanwhile or the
/// Updates section is off. Per-update dedupe key, for the same reason as <see cref="CrmPortalItemSharedNotificationHandler"/>.</summary>
internal sealed class CrmPortalUpdatePublishedNotificationHandler(
    CrmDbContext db, IProjectDirectory projects, INotificationService notifications)
    : IIntegrationEventHandler<CrmPortalUpdatePublishedIntegrationEvent>
{
    public async Task HandleAsync(CrmPortalUpdatePublishedIntegrationEvent @event, CancellationToken ct)
    {
        CrmPortalUpdate? update = await db.CrmPortalUpdates.AsNoTracking().FirstOrDefaultAsync(u => u.Id == @event.UpdateId, ct);
        CrmPortalSettings? settings = await db.CrmPortalSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProjectId == @event.ProjectId, ct);
        if (update is null || !CrmPortalVisibility.CanSeeUpdate(settings, update))
        {
            return;
        }

        PortalClientRecipient? recipient = await PortalClientRecipient.ResolveAsync(db, projects, @event.ProjectId, ct);
        if (recipient is null)
        {
            return;
        }

        await notifications.NotifyAsync(
            recipient.AppUserId,
            NotificationType.CrmPortalUpdatePosted,
            title: update.Title,
            body: $"A new update on {recipient.ProjectName}.",
            entityType: NotificationEntityType.Project,
            entityId: @event.ProjectId,
            dedupeKey: $"crm-portal-update-{update.Id}-published",
            projectId: @event.ProjectId,
            ct: ct);
    }
}

/// <summary>The project's client portal login, when the project is live and its client active with a login.</summary>
internal sealed record PortalClientRecipient(string AppUserId, string ProjectName)
{
    public static async Task<PortalClientRecipient?> ResolveAsync(
        CrmDbContext db, IProjectDirectory projects, Guid projectId, CancellationToken ct)
    {
        ProjectSummary? project = await projects.GetProjectAsync(projectId, ct);
        if (project is null || !project.IsActive)
        {
            return null;
        }

        string? appUserId = await db.Clients.AsNoTracking()
            .Where(c => c.Id == project.ClientId && c.IsActive)
            .Select(c => c.AppUserId)
            .FirstOrDefaultAsync(ct);
        return string.IsNullOrEmpty(appUserId) ? null : new PortalClientRecipient(appUserId, project.Name);
    }
}
