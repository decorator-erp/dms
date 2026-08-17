using BuildingBlocks.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BuildingBlocks.UnitTests;

/// <summary>
/// N1-01 verify for the consolidated sweep. No database: every property asserted here is about the
/// worker's own contract — resolution, isolation, failure containment and interval configuration —
/// and none of it needs a job that actually reads anything.
///
/// <para>The headline case is the boring one: <see cref="RunAllJobs_WithNoRegisteredJobs_IsACleanNoOp"/>.
/// The worker is wired into the composition root at N1-01, four tasks before the first job exists, so
/// "zero registrations is a clean no-op" is what makes that safe rather than merely untested.</para>
/// </summary>
public class PeriodicJobWorkerTests
{
    [Fact]
    public async Task RunAllJobs_WithNoRegisteredJobs_IsACleanNoOp()
    {
        var worker = BuildWorker(new ServiceCollection());

        await worker.RunAllJobsAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RunAllJobs_RunsEveryRegisteredJob_ExactlyOnce()
    {
        var log = new JobLog();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddScoped<IPeriodicJob, FirstJob>();
        services.AddScoped<IPeriodicJob, SecondJob>();
        services.AddScoped<IPeriodicJob, ThirdJob>();

        var worker = BuildWorker(services);
        await worker.RunAllJobsAsync(CancellationToken.None);

        // Ordinal-sorted so this asserts the SET, not the container's enumeration order — the ordering
        // assumption is exactly what resolve-by-name exists to avoid depending on, so asserting it here
        // would re-introduce it in the test.
        Assert.Equal(
            ["FirstJob", "SecondJob", "ThirdJob"],
            log.Ran.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// One failing job must not cost the others their sweep. Without this, the first job to throw
    /// silently cancels every reminder registered after it, and the symptom is "some notifications
    /// stopped" with nothing pointing at the cause.
    /// </summary>
    [Fact]
    public async Task RunAllJobs_ContinuesWithTheRemainingJobs_WhenOneThrows()
    {
        var log = new JobLog();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddScoped<IPeriodicJob, FirstJob>();
        services.AddScoped<IPeriodicJob, ThrowingJob>();
        services.AddScoped<IPeriodicJob, SecondJob>();

        var worker = BuildWorker(services);
        await worker.RunAllJobsAsync(CancellationToken.None);

        Assert.Equal(
            ["FirstJob", "SecondJob", "ThrowingJob"],
            log.Ran.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Each job gets its own DI scope, so a job that poisons its <c>DbContext</c> cannot hand the
    /// broken instance to the next one. Asserted by giving each job a scoped dependency and checking
    /// no two jobs saw the same instance.
    /// </summary>
    [Fact]
    public async Task EachJob_RunsInItsOwnScope()
    {
        var log = new JobLog();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddScoped<ScopeMarker>();
        services.AddScoped<IPeriodicJob, ScopeObservingJobA>();
        services.AddScoped<IPeriodicJob, ScopeObservingJobB>();

        var worker = BuildWorker(services);
        await worker.RunAllJobsAsync(CancellationToken.None);

        Assert.Equal(2, log.ObservedScopes.Count);
        Assert.Equal(2, log.ObservedScopes.Distinct().Count());
    }

    [Fact]
    public void Interval_DefaultsToFifteenMinutes_WhenUnconfigured()
    {
        var worker = BuildWorker(new ServiceCollection());

        Assert.Equal(TimeSpan.FromMinutes(15), worker.Interval);
    }

    [Fact]
    public void Interval_HonoursSweepMinutes_WhenConfiguredToAPositiveInteger()
    {
        var worker = BuildWorker(new ServiceCollection(), sweepMinutes: "45");

        Assert.Equal(TimeSpan.FromMinutes(45), worker.Interval);
    }

    /// <summary>
    /// A non-positive interval is not a slow sweep — <c>new PeriodicTimer(TimeSpan.Zero)</c> throws,
    /// so <c>Notifications:SweepMinutes=0</c> without this guard takes the host down at the first tick.
    /// Degrade to the default instead.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("")]
    [InlineData("fifteen")]
    [InlineData("15.5")]
    public void Interval_FallsBackToTheDefault_WhenSweepMinutesIsNotAPositiveInteger(string configured)
    {
        var worker = BuildWorker(new ServiceCollection(), sweepMinutes: configured);

        Assert.Equal(TimeSpan.FromMinutes(15), worker.Interval);
    }

    private static PeriodicJobWorker BuildWorker(IServiceCollection services, string? sweepMinutes = null)
    {
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        var provider = services.BuildServiceProvider();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:SweepMinutes"] = sweepMinutes,
            })
            .Build();

        return new PeriodicJobWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            NullLogger<PeriodicJobWorker>.Instance);
    }

    private sealed class JobLog
    {
        private readonly Lock _gate = new();

        public List<string> Ran { get; } = [];

        public List<Guid> ObservedScopes { get; } = [];

        public void Record(string jobName)
        {
            lock (_gate)
                Ran.Add(jobName);
        }

        public void RecordScope(Guid scopeId)
        {
            lock (_gate)
                ObservedScopes.Add(scopeId);
        }
    }

    /// <summary>A scoped dependency whose identity reveals which scope resolved it.</summary>
    private sealed class ScopeMarker
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    private abstract class RecordingJob(JobLog log) : IPeriodicJob
    {
        // A property rather than a captured primary-constructor parameter: ThrowingJob overrides
        // RunAsync and needs the log too, and using its OWN primary parameter there would capture the
        // same value into both the derived state and the base (CS9107).
        protected JobLog Log { get; } = log;

        public abstract string Name { get; }

        public virtual Task RunAsync(CancellationToken ct)
        {
            Log.Record(Name);
            return Task.CompletedTask;
        }
    }

    private sealed class FirstJob(JobLog log) : RecordingJob(log)
    {
        public override string Name => "FirstJob";
    }

    private sealed class SecondJob(JobLog log) : RecordingJob(log)
    {
        public override string Name => "SecondJob";
    }

    private sealed class ThirdJob(JobLog log) : RecordingJob(log)
    {
        public override string Name => "ThirdJob";
    }

    private sealed class ThrowingJob(JobLog log) : RecordingJob(log)
    {
        public override string Name => "ThrowingJob";

        public override Task RunAsync(CancellationToken ct)
        {
            Log.Record(Name);
            throw new InvalidOperationException("This job always fails.");
        }
    }

    private sealed class ScopeObservingJobA(JobLog log, ScopeMarker marker) : IPeriodicJob
    {
        public string Name => "ScopeObservingJobA";

        public Task RunAsync(CancellationToken ct)
        {
            log.RecordScope(marker.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class ScopeObservingJobB(JobLog log, ScopeMarker marker) : IPeriodicJob
    {
        public string Name => "ScopeObservingJobB";

        public Task RunAsync(CancellationToken ct)
        {
            log.RecordScope(marker.Id);
            return Task.CompletedTask;
        }
    }
}
