using BuildingBlocks.Jobs;
using Crm.Application.Features.Periods;
using Crm.Contracts;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Crm.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Notifications.Contracts;
using Projects.Contracts;
using SharedKernel;
using RequirementPeriodStatus = Crm.Domain.RequirementPeriodStatus;

namespace Crm.Application.Features.Jobs;

/// <summary>
/// Closes requirement-gathering windows whose target close time has passed, then tells the client.
///
/// <para><b>The only job here that mutates anything.</b> The other six read and notify; this one is the
/// scheduled half of a real state transition, and closing an overdue window is CRM's own data, so it
/// belongs to CRM whether or not a notification follows.</para>
///
/// <para><b>It closes through the same <see cref="CrmPeriodCloser"/> the manual path does</b> (B3),
/// so the Draft and Rejected items it drops from the gate publish <c>CrmItemsResolvedIntegrationEvent</c>
/// for the scopes they resolve, exactly as a manual close.</para>
///
/// <para><b>It publishes the same integration event the manual path does.</b>
/// <c>CloseCrmRequirementPeriodCommandHandler</c> enqueues <c>CrmPeriodClosedIntegrationEvent</c> when
/// the period is stage-scoped, because Projects subscribes to satisfy a <c>CrmPeriodClosed</c> gate. A
/// period that closes on the clock must satisfy that gate exactly as one closed by hand — omitting the
/// event here would make a gate's behaviour depend on WHO closed the window, which is the kind of
/// divergence nobody discovers until a project stalls. A project-wide period (no stage) has no gate to
/// satisfy and publishes nothing, matching the handler.</para>
///
/// <para><b>Three guards, and they are not redundant.</b> <c>Close()</c> refuses a period that is not
/// Open (so a race with the manual command loses harmlessly); the dedupe key makes the notification
/// idempotent; and <c>MarkCloseNotified</c> records that the client was told. The domain method is
/// itself idempotent, so a crash anywhere in the sequence resumes safely on the next tick: a period still Open
/// is closed again, and one Closed without <c>CloseNotifiedAtUtc</c> has its notify step retried
/// (FINDING-P1-3).</para>
/// </summary>
internal sealed class CrmPeriodCloseJob(
    CrmDbContext db,
    IClientDirectory clientDirectory,
    IProjectDirectory projectDirectory,
    INotificationService notifications,
    CrmApprovalOutcomes outcomes,
    TimeProvider timeProvider,
    ILogger<CrmPeriodCloseJob> logger) : IPeriodicJob
{
    /// <summary>FINDING-P1-12: how far back the notify retry looks. Only a period closed within this window
    /// (<c>ClosedAtUtc &gt;= now - RetryWindow</c>) has a failed notify retried. The job runs far more often than
    /// this, so a transient failure gets many retries inside it; without a bound, the first run after a deploy
    /// would tell every client whose period was ever closed after its due date — months-old ones included —
    /// and a period whose client has no portal user (nothing to notify, so never stamped) would be rescanned
    /// on every run forever. Two days: long enough to ride out an outage of a weekend's length, short enough
    /// that a "the window has closed" message is still news.</summary>
    internal static readonly TimeSpan RetryWindow = TimeSpan.FromDays(2);

    public string Name => nameof(CrmPeriodCloseJob);

    public async Task RunAsync(CancellationToken ct)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;

        List<Guid> dueIds = await Due(now).OrderBy(p => p.ClosesAtUtc).Select(p => p.Id).ToListAsync(ct);
        // FINDING-P1-3: periods a previous run closed but never got to tell the client about (the lookup or
        // the notification threw after the close committed). Scanned before this run closes anything, so a
        // period closed below is not notified twice in one run.
        List<Guid> unnotifiedIds = await ClosedUnnotified(now).OrderBy(p => p.ClosedAtUtc).Select(p => p.Id).ToListAsync(ct);

        foreach (Guid periodId in dueIds)
        {
            // L-3: one period failing (an xmin conflict with a concurrent extend or close, a notification
            // error) is logged and costs that period only; the run moves on to the next due period, as
            // CrmMeetingReminderJob does per meeting. Whatever it left tracked is dropped with it.
            await RunIsolatedAsync(periodId, () => CloseAndNotifyAsync(periodId, now, ct));
        }

        foreach (Guid periodId in unnotifiedIds)
        {
            await RunIsolatedAsync(periodId, () => NotifyClosedAsync(periodId, now, ct));
        }
    }

    private async Task RunIsolatedAsync(Guid periodId, Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Whichever step threw is retried next run: a close that did not commit leaves the period Open
            // (Due), a notification that did not happen leaves CloseNotifiedAtUtc null (ClosedUnnotified).
            logger.LogError(ex, "Closing or notifying requirement period {PeriodId} failed; the step that did not complete is retried on the next run.", periodId);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private IQueryable<CrmRequirementPeriod> Due(DateTime now) =>
        db.CrmRequirementPeriods.Where(p => p.IsActive
            && p.Status == RequirementPeriodStatus.Open
            && p.ClosesAtUtc <= now);

    /// <summary>Closed on or after its target time (so by this job, or by hand once overdue) and the client
    /// not yet told. A period closed by hand before its target time is not this job's to announce — the
    /// manual close never notified — so it is excluded. Bounded by <see cref="RetryWindow"/> (FINDING-P1-12).</summary>
    private IQueryable<CrmRequirementPeriod> ClosedUnnotified(DateTime now)
    {
        DateTime retryFrom = now - RetryWindow;
        return db.CrmRequirementPeriods.Where(p => p.IsActive
            && p.Status == RequirementPeriodStatus.Closed
            && p.CloseNotifiedAtUtc == null
            && p.ClosedAtUtc >= p.ClosesAtUtc
            && p.ClosedAtUtc >= retryFrom);
    }

    private async Task CloseAndNotifyAsync(Guid periodId, DateTime now, CancellationToken ct)
    {
        // Re-filtered: a period closed or extended since the id scan is no longer due.
        CrmRequirementPeriod? period = await Due(now).SingleOrDefaultAsync(p => p.Id == periodId, ct);
        if (period is null)
        {
            return;
        }

        // B3: the same close sequence as the manual command, so an expired period whose last open
        // items were Draft or Rejected announces the scopes it resolved. The job has no command
        // pipeline, so it owns the transaction the close and its events commit in.
        Result closed;
        await using (IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct))
        {
            closed = await CrmPeriodCloser.CloseAsync(db, outcomes, period, now, ct);
            if (closed.IsSuccess)
            {
                await transaction.CommitAsync(ct);
            }
        }

        if (closed.IsFailure)
        {
            // Lost a race with another actor between the read and here (already closed, or an xmin 409).
            logger.LogWarning("Requirement period {PeriodId} was not closed by the job: {Code}.", periodId, closed.Error.Code);
            return;
        }

        await NotifyAndStampAsync(period, now, ct);
    }

    /// <summary>The retry of the notify step alone, for a period an earlier run closed.</summary>
    private async Task NotifyClosedAsync(Guid periodId, DateTime now, CancellationToken ct)
    {
        // Re-filtered: a concurrent run may have stamped it since the id scan.
        CrmRequirementPeriod? period = await ClosedUnnotified(now).SingleOrDefaultAsync(p => p.Id == periodId, ct);
        if (period is not null)
        {
            await NotifyAndStampAsync(period, now, ct);
        }
    }

    private async Task NotifyAndStampAsync(CrmRequirementPeriod period, DateTime now, CancellationToken ct)
    {
        ProjectSummary? project = await projectDirectory.GetProjectAsync(period.ProjectId, ct);
        ClientSummary? client = project is null ? null : await clientDirectory.GetAsync(project.ClientId, ct);

        if (!string.IsNullOrEmpty(client?.AppUserId))
        {
            // The dedupe key makes a retry after a lost stamp a no-op on the Notifications side.
            await notifications.NotifyAsync(
                client.AppUserId,
                NotificationType.CrmPeriodClosed,
                title: "Requirement gathering is finished",
                body: "The gathering window has closed. If you have any final requirement, tell us now.",
                entityType: NotificationEntityType.RequirementPeriod,
                entityId: period.Id,
                dedupeKey: $"crm-period-{period.Id}-closed",
                ct: ct);

            period.MarkCloseNotified(now);
            // L-3: through ItemWrites, so a concurrent change to the row is a logged 409, not a thrown run.
            Result saved = await ItemWrites.SaveAsync(db, ct, CrmPeriodErrors.ConcurrentUpdate);
            if (saved.IsFailure)
            {
                logger.LogWarning("Requirement period {PeriodId} closed, but its notified stamp was not saved: {Code}.", period.Id, saved.Error.Code);
            }
        }
    }
}
