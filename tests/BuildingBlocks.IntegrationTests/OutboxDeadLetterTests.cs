using System.Data.Common;
using BuildingBlocks.Events;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace BuildingBlocks.IntegrationTests;

/// <summary>
/// Phase 5 (5.1): the outbox backs off between attempts, dead-letters a message at
/// <c>Outbox:MaxAttempts</c> or when its payload can't be read, retries a type it can't resolve, and keeps
/// sweeping past either. Each test gets a database of its own in the shared container, so another test's
/// leftover messages can't change the counts asserted here.
/// </summary>
[Collection(OutboxCollection.Name)]
public sealed class OutboxDeadLetterTests(OutboxFixture fixture)
{
    [Fact]
    public async Task A_failing_message_backs_off_doubling_then_is_dead_lettered_at_max_attempts_with_one_error_log()
    {
        string cs = await NewDatabaseAsync();
        var clock = new ManualTimeProvider();
        var failure = new FailureController { AlwaysFail = true };
        var log = new CapturingLogger();
        await using ServiceProvider provider = BuildProvider(cs, new CallCounter(), failure, clock, log);
        Guid id = await EnqueueAsync(provider);

        List<TimeSpan> waits = [];
        for (int attempt = 1; attempt <= 15; attempt++)
        {
            await SweepAsync(provider);
            OutboxMessage message = await ReadAsync(cs, id);
            Assert.Equal(attempt, message.Attempts);

            if (attempt == 15)
            {
                Assert.NotNull(message.DeadLetteredUtc);
                break;
            }

            Assert.Null(message.DeadLetteredUtc);
            TimeSpan wait = message.NextAttemptUtc - clock.GetUtcNow();
            waits.Add(wait);

            // Not picked up a second before it is due.
            clock.Advance(wait - TimeSpan.FromSeconds(1));
            await SweepAsync(provider);
            Assert.Equal(attempt, (await ReadAsync(cs, id)).Attempts);
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(
            [30, 60, 120, 240, 480, 960, 1920, 3600, 3600, 3600, 3600, 3600, 3600, 3600],
            waits.Select(w => w.TotalSeconds));
        Assert.Equal(29_010, waits.Sum(w => w.TotalSeconds));
        Assert.Equal(1, log.Count(LogLevel.Error));
        Assert.Equal(14, log.Count(LogLevel.Warning));

        // A dead letter is never selected again, however long the wait.
        clock.Advance(TimeSpan.FromDays(30));
        await SweepAsync(provider);
        Assert.Equal(15, (await ReadAsync(cs, id)).Attempts);
        Assert.Equal(1, log.Count(LogLevel.Error));
    }

    [Fact]
    public async Task A_payload_that_does_not_fit_its_type_is_dead_lettered_at_once_and_the_sweep_goes_on()
    {
        string cs = await NewDatabaseAsync();
        var clock = new ManualTimeProvider();
        var counter = new CallCounter();
        var log = new CapturingLogger();
        await using ServiceProvider provider = BuildProvider(cs, counter, new FailureController(), clock, log);

        var poison = Guid.NewGuid();
        await InsertRawAsync(cs, poison, typeof(TestEvent).AssemblyQualifiedName!, """{"eventId":"not-a-guid"}""",
            clock.GetUtcNow().AddMinutes(-5));
        Guid good = await EnqueueAsync(provider);

        await SweepAsync(provider);

        OutboxMessage dead = await ReadAsync(cs, poison);
        Assert.NotNull(dead.DeadLetteredUtc);
        Assert.Equal(1, dead.Attempts);
        Assert.Contains("could not be deserialized", dead.Error);
        Assert.NotNull((await ReadAsync(cs, good)).ProcessedUtc);
        Assert.Equal(1, counter.Count);
        Assert.Equal(1, log.Count(LogLevel.Error));

        clock.Advance(TimeSpan.FromDays(1));
        await SweepAsync(provider);
        Assert.Equal(1, (await ReadAsync(cs, poison)).Attempts);
    }

    /// <summary>H4: an older instance in an overlapping deploy can't resolve a type only the newer build
    /// has. That message is valid, so it waits out the backoff like any failure instead of being
    /// dead-lettered, and the sweep still delivers the message behind it.</summary>
    [Fact]
    public async Task An_unknown_type_is_retried_with_backoff_not_dead_lettered()
    {
        string cs = await NewDatabaseAsync();
        var clock = new ManualTimeProvider();
        var counter = new CallCounter();
        await using ServiceProvider provider = BuildProvider(cs, counter, new FailureController(), clock, new CapturingLogger());

        var unknown = Guid.NewGuid();
        await InsertRawAsync(cs, unknown, "Future.Module.Contracts.SomeNewIntegrationEvent, Future.Module.Contracts", "{}",
            clock.GetUtcNow().AddMinutes(-5));
        Guid good = await EnqueueAsync(provider);

        await SweepAsync(provider);

        OutboxMessage first = await ReadAsync(cs, unknown);
        Assert.Null(first.DeadLetteredUtc);
        Assert.Null(first.ProcessedUtc);
        Assert.Equal(1, first.Attempts);
        Assert.Equal(TimeSpan.FromSeconds(30), first.NextAttemptUtc - clock.GetUtcNow());
        Assert.NotNull((await ReadAsync(cs, good)).ProcessedUtc);

        clock.Advance(TimeSpan.FromSeconds(31));
        await SweepAsync(provider);

        OutboxMessage second = await ReadAsync(cs, unknown);
        Assert.Equal(2, second.Attempts);
        Assert.Null(second.DeadLetteredUtc);
        Assert.Equal(TimeSpan.FromSeconds(60), second.NextAttemptUtc - clock.GetUtcNow());
    }

    /// <summary>H17: the notes' recovery SQL, run as written, brings a dead letter back. A reset that
    /// set <c>next_attempt_utc</c> to NULL would never be selected again.</summary>
    [Fact]
    public async Task A_dead_letter_reset_with_the_recovery_sql_is_delivered_on_the_next_sweep()
    {
        string cs = await NewDatabaseAsync();
        var failure = new FailureController { AlwaysFail = true };
        var counter = new CallCounter();
        await using ServiceProvider provider = BuildProvider(cs, counter, failure, clock: null, new CapturingLogger(),
            new Dictionary<string, string?> { ["Outbox:MaxAttempts"] = "1" });
        Guid id = await EnqueueAsync(provider);

        await SweepAsync(provider);
        Assert.NotNull((await ReadAsync(cs, id)).DeadLetteredUtc);

        // The cause is fixed, then the operator resets the row.
        failure.AlwaysFail = false;
        await ExecuteAsync(cs, $"""
            UPDATE shared.outbox_messages
            SET dead_lettered_utc = NULL, attempts = 0, next_attempt_utc = now()
            WHERE id = '{id}';
            """);

        await SweepAsync(provider);

        OutboxMessage message = await ReadAsync(cs, id);
        Assert.NotNull(message.ProcessedUtc);
        Assert.Null(message.DeadLetteredUtc);
        Assert.Equal(2, counter.Count);
    }

    /// <summary>R17: adding the column with its default stamps every existing row with the migration
    /// time; the backfill must then put pending rows back to when they occurred.</summary>
    [Fact]
    public async Task The_migration_backfills_pending_rows_from_occurred_utc_and_leaves_none_null()
    {
        string cs = await NewDatabaseAsync(migrate: false);
        await using (SharedDbContext shared = OpenShared(cs))
        {
            await shared.GetService<IMigrator>().MigrateAsync("20260817094754_AddAuditLogs");
        }

        var occurred = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var pending = Guid.NewGuid();
        var processed = Guid.NewGuid();
        await ExecuteAsync(cs, $"""
            INSERT INTO shared.outbox_messages (id, type, content, occurred_utc, processed_utc, attempts)
            VALUES ('{pending}', 't', '{"{}"}', '{occurred:O}', NULL, 3),
                   ('{processed}', 't', '{"{}"}', '{occurred:O}', '{occurred.AddMinutes(1):O}', 0);
            """);

        await using (SharedDbContext shared = OpenShared(cs))
        {
            await shared.Database.MigrateAsync();
        }

        Assert.Equal(occurred, (await ReadAsync(cs, pending)).NextAttemptUtc);
        Assert.True((await ReadAsync(cs, processed)).NextAttemptUtc > occurred.AddDays(1));
        Assert.Equal(0L, await ScalarAsync(cs, "SELECT count(*) FROM shared.outbox_messages WHERE next_attempt_utc IS NULL"));
    }

    /// <summary>H16: the sweep's own SQL, captured as sent, can be answered from the partial index.
    /// Sequential scans are switched off for the EXPLAIN because on a near-empty table the planner would
    /// pick one anyway; what matters is that the predicate lets the index be used at all, which an
    /// <c>IS NULL OR</c> on next_attempt_utc would not.</summary>
    [Fact]
    public async Task The_sweep_query_can_use_the_unprocessed_index()
    {
        string cs = await NewDatabaseAsync();
        var capture = new SweepCommandCapture();
        await using ServiceProvider provider = BuildProvider(cs, new CallCounter(), new FailureController(), clock: null,
            new CapturingLogger(), interceptor: capture);

        await SweepAsync(provider);
        Assert.NotNull(capture.Command);

        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await using (NpgsqlCommand off = new("SET enable_seqscan = off", connection))
        {
            await off.ExecuteNonQueryAsync();
        }

        await using var explain = new NpgsqlCommand("EXPLAIN " + capture.Command!.Value.Text, connection);
        foreach ((string name, object? value) in capture.Command.Value.Parameters)
        {
            explain.Parameters.Add(new NpgsqlParameter(name, value ?? DBNull.Value));
        }

        List<string> plan = [];
        await using (NpgsqlDataReader reader = await explain.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                plan.Add(reader.GetString(0));
            }
        }

        Assert.Contains(plan, line => line.Contains("ix_outbox_messages_unprocessed", StringComparison.Ordinal));
    }

    private static ServiceProvider BuildProvider(
        string cs,
        CallCounter counter,
        FailureController failure,
        TimeProvider? clock,
        CapturingLogger log,
        Dictionary<string, string?>? settings = null,
        IInterceptor? interceptor = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(counter);
        services.AddSingleton(failure);
        if (clock is not null)
        {
            // Before AddOutbox, whose TryAdd would otherwise register the system clock.
            services.AddSingleton(clock);
        }

        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ILogger<OutboxProcessor>>(log);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build());
        services.AddDbContext<SharedDbContext>(o =>
        {
            o.UseNpgsql(cs);
            if (interceptor is not null)
            {
                o.AddInterceptors(interceptor);
            }
        });
        services.AddDbContext<TestModuleDbContext>(o => o.UseNpgsql(cs));
        services.AddOutbox();
        services.AddSingleton<OutboxProcessor>();
        services.AddModuleIntegrationEvents<TestModuleDbContext>(typeof(TestEventHandler).Assembly);
        return services.BuildServiceProvider();
    }

    private static async Task<Guid> EnqueueAsync(ServiceProvider provider)
    {
        var @event = new TestEvent(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1));
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        TestModuleDbContext db = scope.ServiceProvider.GetRequiredService<TestModuleDbContext>();
        db.Outbox().Enqueue(@event);
        await db.SaveChangesAsync();
        return @event.EventId;
    }

    private static async Task SweepAsync(ServiceProvider provider) =>
        await provider.GetRequiredService<OutboxProcessor>().ProcessPendingAsync(CancellationToken.None);

    private static SharedDbContext OpenShared(string cs) =>
        new(new DbContextOptionsBuilder<SharedDbContext>().UseNpgsql(cs).Options);

    private static async Task<OutboxMessage> ReadAsync(string cs, Guid id)
    {
        await using SharedDbContext shared = OpenShared(cs);
        return await shared.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    private static Task InsertRawAsync(string cs, Guid id, string type, string content, DateTimeOffset occurred) =>
        ExecuteAsync(cs, $"""
            INSERT INTO shared.outbox_messages (id, type, content, occurred_utc, attempts, next_attempt_utc)
            VALUES ('{id}', '{type}', '{content}', '{occurred:O}', 0, '{occurred:O}');
            """);

    private async Task<string> NewDatabaseAsync(bool migrate = true)
    {
        string name = $"deadletter_{Guid.NewGuid():N}";
        await ExecuteAsync(fixture.ConnectionString, $"CREATE DATABASE {name}");
        string cs = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = name }.ConnectionString;
        if (migrate)
        {
            await using SharedDbContext shared = OpenShared(cs);
            await shared.Database.MigrateAsync();
            await using var module = new TestModuleDbContext(
                new DbContextOptionsBuilder<TestModuleDbContext>().UseNpgsql(cs).Options);
            await module.Database.MigrateAsync();
        }

        return cs;
    }

    private static async Task ExecuteAsync(string cs, string sql)
    {
        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(string cs, string sql)
    {
        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    /// <summary>Keeps the first command that selects due outbox ids, as sent, with its parameters.</summary>
    private sealed class SweepCommandCapture : DbCommandInterceptor
    {
        public (string Text, List<(string Name, object? Value)> Parameters)? Command { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Command is null && command.CommandText.Contains("next_attempt_utc", StringComparison.Ordinal))
            {
                Command = (command.CommandText,
                    command.Parameters.Cast<DbParameter>().Select(p => (p.ParameterName, (object?)p.Value)).ToList());
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}

/// <summary>Counts the processor's log entries by level.</summary>
public sealed class CapturingLogger : ILogger<OutboxProcessor>
{
    private readonly List<LogLevel> _levels = [];

    public int Count(LogLevel level)
    {
        lock (_levels)
        {
            return _levels.Count(l => l == level);
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_levels)
        {
            _levels.Add(logLevel);
        }
    }
}
