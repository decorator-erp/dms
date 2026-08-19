using BuildingBlocks.Jobs;
using Crm.Contracts;
using Finance.Domain;
using Finance.Infrastructure.Persistence;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Notifications.Contracts;
using Projects.Contracts;

namespace Finance.Application.Features.Jobs;

/// <summary>
/// Invoice payment reminders: −5d, −1d, due day, +1d, +7d, +30d → the project owner and the client.
/// The eighth <see cref="IPeriodicJob"/>, and the one deferred from Phase 6 because it reads
/// <c>Invoices</c>, which did not exist then.
///
/// <para><b>Tier 0 classifies as DueSoon, not Overdue.</b> An invoice due today is not yet late, and the
/// backend's own <c>tier &gt; 0</c> test says so. Writing <c>tier &gt;= 0</c> would tell every client
/// their invoice is overdue on the morning it falls due.</para>
///
/// <para><b>The window is load-bearing for cost, not decoration.</b> <c>[now−60d, now+5d]</c> keeps this
/// from re-reading every invoice ever issued on every tick; the dedupe key already stops repeats, so the
/// bound costs nothing in correctness. The lower bound is wider than the largest tier (+30d) on purpose,
/// so an invoice whose +30d tier was missed during downtime still gets it.</para>
///
/// <para><b>THE RECIPIENT RULE, and its recorded divergence (R1).</b> The backend notifies "finance
/// staff", defined in <c>RecipientResolver</c> as every user whose role carries the <c>finance.read</c>
/// permission claim. <b>That cannot be ported.</b> <c>Identity.Contracts</c> has exactly three surfaces
/// — <c>IUserDirectory</c> (by id), <c>IUserAdministration</c>, and <c>IPermissionCatalog.All()</c>
/// which returns permission STRINGS — and none enumerates users holding a permission. Adding
/// <c>GetUsersWithPermissionAsync</c> was considered and rejected: it is a new cross-module read surface
/// invented to serve one job.
///
/// So recipients are the <b>project owner</b> (<c>Invoice.ProjectId</c> → <c>IProjectDirectory</c> →
/// <c>OwnerId</c> → <c>IHrDirectory</c> → <c>AppUserId</c>) plus the <b>client user</b>
/// (<c>Invoice.ClientId</c> → <c>IClientDirectory</c> → <c>AppUserId</c>), deduped, skipping the invoice
/// when the set is empty. <c>ProjectSummary.OwnerId</c> was added at F1-00b for exactly this hop — the
/// owner half is not reachable otherwise, since a job living in Finance cannot read
/// <c>projects.projects</c>.
///
/// <b>What is lost:</b> finance staff who are not the project owner get no invoice-due reminder.
/// Deliberate, and arguably an improvement — a broadcast to every <c>finance.read</c> holder on every
/// overdue invoice is the notification-fatigue shape. <b>Vendor invoices carry no <c>ClientId</c></b>
/// (<c>VendorName</c> is free text and a vendor has no account here), so they reach the project owner
/// only; notifying the project's CLIENT about a vendor invoice would be wrong and is not done.</para>
///
/// <para><b>Idempotence is the dedupe key</b>, per <see cref="IPeriodicJob"/>'s contract:
/// <c>invoice-{id}-due-{tier}d</c>, stored per recipient, so a restart mid-sweep or an overlapping tick
/// re-runs this and duplicates nothing.</para>
/// </summary>
internal sealed class InvoiceDueJob(
    FinanceDbContext db,
    IProjectDirectory projects,
    IHrDirectory hrDirectory,
    IClientDirectory clientDirectory,
    INotificationService notifications,
    TimeProvider timeProvider) : IPeriodicJob
{
    private static readonly int[] Tiers = [-5, -1, 0, 1, 7, 30];

    public string Name => nameof(InvoiceDueJob);

    public async Task RunAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);

        var windowStart = today.AddDays(-60); // bound the overdue scan; dedupe already stops repeats.
        var windowEnd = today.AddDays(5);     // earliest tier is -5d.

        var invoices = await db.Invoices
            .AsNoTracking()
            .Where(i => i.IsActive
                        && i.Status == InvoiceStatus.Open
                        && i.DueOn != null
                        && i.DueOn >= windowStart && i.DueOn <= windowEnd
                        && i.TotalAmount - i.PaidAmount > 0)
            .Select(i => new
            {
                i.Id,
                i.Number,
                i.ProjectId,
                i.ClientId,
                Due = i.DueOn!.Value,
                Balance = i.TotalAmount - i.PaidAmount,
            })
            .ToListAsync(ct);

        foreach (var invoice in invoices)
        {
            var due = invoice.Due.ToDateTime(TimeOnly.MinValue);

            if (DeadlineTiers.MostRelevant(now, due, Tiers) is not { } tier)
                continue;

            var targets = await RecipientsAsync(invoice.ProjectId, invoice.ClientId, ct);
            if (targets.Count == 0)
                continue;

            await notifications.NotifyManyAsync(
                targets,
                // Tier 0 is DueSoon: an invoice due today is not yet late.
                tier > 0 ? NotificationType.InvoiceOverdue : NotificationType.InvoiceDueSoon,
                title: $"Invoice {DeadlineTiers.Phrase(tier)} — {invoice.Number}",
                body: $"Invoice '{invoice.Number}' is {DeadlineTiers.Phrase(tier)} "
                      + $"(due {invoice.Due:yyyy-MM-dd}, outstanding {invoice.Balance}).",
                entityType: NotificationEntityType.Invoice,
                entityId: invoice.Id,
                dedupeKey: $"invoice-{invoice.Id}-due-{tier}d",
                ct: ct);
        }
    }

    /// <summary>The R1 rule, in one place so the two-hop owner lookup cannot drift from the one-hop
    /// client lookup. A missing owner or client is skipped rather than failing the sweep — one
    /// unresolvable recipient must not stop every other invoice being reminded.</summary>
    private async Task<List<string>> RecipientsAsync(Guid projectId, Guid? clientId, CancellationToken ct)
    {
        string? ownerUserId = null;

        if (await projects.GetProjectAsync(projectId, ct) is { } project
            && await hrDirectory.GetAsync(project.OwnerId, ct) is { } owner)
        {
            ownerUserId = owner.AppUserId;
        }

        string? clientUserId = null;

        if (clientId is Guid id && await clientDirectory.GetAsync(id, ct) is { } client)
            clientUserId = client.AppUserId;

        string?[] candidates = [ownerUserId, clientUserId];

        return [.. candidates
            .Where(u => !string.IsNullOrEmpty(u))
            .Select(u => u!)
            .Distinct(StringComparer.Ordinal)];
    }
}
