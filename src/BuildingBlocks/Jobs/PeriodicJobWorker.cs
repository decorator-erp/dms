using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Jobs;

/// <summary>
/// Runs every registered <see cref="IPeriodicJob"/> sequentially on one timer. This is the "nine loops
/// become one" of <c>docs/low-resource-hosting.md</c> §3, now implemented: the backend runs a
/// <c>BackgroundService</c> per job, each scanning tables on its own schedule, which is the single
/// biggest avoidable always-on cost in the old design.
///
/// <para>Each job gets its own DI scope and its own try/catch — one failing job never stops the
/// others, and a poisoned <c>DbContext</c> never leaks across jobs. Jobs are idempotent by dedupe key,
/// so overlap after a restart is harmless.</para>
///
/// <para><b>Interval:</b> <c>Notifications:SweepMinutes</c>, default 15. Deadline-class reminders do
/// not need second-level accuracy, and the hosting target is one small instance. A non-positive or
/// unparseable value falls back to the default rather than producing a hot loop.</para>
/// </summary>
public sealed class PeriodicJobWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<PeriodicJobWorker> logger) : BackgroundService
{
    private const int DefaultSweepMinutes = 15;

    // Same reasoning as OutboxProcessor._sweepGate: RunAllJobsAsync is public so tests (and a manual
    // "run now") can drive it, which makes this loop and that caller two independent entry points into
    // the same job set. Jobs are idempotent, so an interleave cannot duplicate a notification - but a
    // test that returns while the loop's un-awaited sweep is still mid-flight can still read state
    // that sweep has not committed. Single-flighting removes the interleave rather than reasoning
    // about which half of it is observable.
    private readonly SemaphoreSlim _sweepGate = new(1, 1);

    /// <summary>The resolved sweep interval, after the config read and the fallback. Exposed because a
    /// misconfigured value is otherwise invisible until the timer either never fires or throws on
    /// construction — <c>new PeriodicTimer(TimeSpan.Zero)</c> is an <c>ArgumentOutOfRangeException</c>,
    /// so <c>Notifications:SweepMinutes=0</c> would take the host down at first tick rather than
    /// degrade.</summary>
    public TimeSpan Interval { get; } = TimeSpan.FromMinutes(
        int.TryParse(configuration["Notifications:SweepMinutes"], out int minutes) && minutes > 0
            ? minutes
            : DefaultSweepMinutes);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!BackgroundLoops.Enabled(configuration))
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);

        try
        {
            // do/while, not while: the first sweep runs at startup rather than one interval later.
            do
            {
                await RunAllJobsAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Host shutdown.
        }
    }

    /// <summary>Runs every registered job once, without the wait loop. Public so tests (and a manual
    /// "run now" trigger) can drive it directly — the same reason
    /// <c>OutboxProcessor.ProcessPendingAsync</c> is public.</summary>
    public async Task RunAllJobsAsync(CancellationToken ct)
    {
        await _sweepGate.WaitAsync(ct);
        try
        {
            // Enumerate names in a throwaway scope, then re-resolve each job BY NAME in its own scope.
            //
            // The backend resolves by INDEX across independent scopes
            // (backend/Infrastructure/Notifications/NotificationJobsWorker.cs) - GetServices in scope A
            // to count, then GetServices().ElementAt(i) in scope B to run. That silently assumes
            // GetServices returns a stable order across two independently-built scopes. It happens to,
            // today, in this container - but nothing states it and nothing tests it, and the failure
            // mode if it ever stopped holding is a job silently never running while another runs twice.
            //
            // Resolving by name costs one extra materialization of the job set per job (N sets for N
            // jobs). With 7 jobs that is ~35 DbContext constructions per sweep, once every 15 minutes,
            // and a DbContext constructor opens no connection. Paying that to delete an unstated
            // ordering assumption is the right trade.
            List<string> jobNames;
            await using (AsyncServiceScope probeScope = scopeFactory.CreateAsyncScope())
            {
                jobNames = [.. probeScope.ServiceProvider.GetServices<IPeriodicJob>().Select(job => job.Name)];
            }

            if (jobNames.Count == 0)
            {
                return;
            }

            var duplicates = jobNames.GroupBy(name => name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();
            if (duplicates.Count > 0)
            {
                // Two jobs sharing a Name means one of them never runs, since the re-resolve below picks
                // the first match. Loud, because the symptom otherwise is a reminder that just stops.
                logger.LogError(
                    "Duplicate IPeriodicJob names registered: {Duplicates}. Jobs sharing a name will not all run.",
                    string.Join(", ", duplicates));
            }

            foreach (string jobName in jobNames)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                IPeriodicJob? job = scope.ServiceProvider.GetServices<IPeriodicJob>()
                    .FirstOrDefault(candidate => string.Equals(candidate.Name, jobName, StringComparison.Ordinal));

                if (job is null)
                {
                    logger.LogWarning("Periodic job {Job} was enumerated but could not be resolved — skipping.", jobName);
                    continue;
                }

                try
                {
                    await job.RunAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Periodic job {Job} failed — continuing with the next job.", jobName);
                }
            }
        }
        finally
        {
            _sweepGate.Release();
        }
    }
}
