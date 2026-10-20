using System.Text.Json;
using BuildingBlocks.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Outbox;

/// <summary>
/// Waits on <see cref="OutboxSignal"/> for either a wake-on-write nudge or the fallback interval,
/// then polls <c>shared.outbox_messages</c> for unprocessed rows and publishes each through
/// <see cref="IEventBus"/>. Each message is re-fetched and processed in its own scope, so one
/// message's failure can't affect another's.
///
/// The wake-on-write nudge (via <see cref="InboxDedupeDecorator{TContext,TEvent}"/> and
/// <c>DomainEventDispatchDecorator{TContext}</c>, both after their own commit) makes delivery near-
/// instant on the happy path. The fallback interval — <c>Outbox:FallbackPollSeconds</c>, default 60 —
/// is what still delivers a row from a crashed request (whose nudge never fired) or a second process
/// instance (whose nudge this one never observes); see docs/low-resource-hosting.md §2. Never remove
/// the fallback in exchange for the signal — the signal is the optimization, the fallback is the
/// correctness guarantee.
///
/// An event with no registered handler is marked processed on the first pass — <see cref="EventBus"/>
/// resolving zero handlers is a successful no-op, not a stall. An event whose handler throws is left
/// unprocessed with attempts/error recorded, for a later poll to retry; a handler that already
/// committed its own effect (and its own inbox row, atomically — see
/// <see cref="InboxDedupeDecorator{TContext,TEvent}"/>) is skipped on that retry without redoing its
/// work.
///
/// <para><b>States.</b> <c>pending → processed | (failed → waiting(backoff) → pending)* → dead-lettered</c>.
/// A failure puts the message off by <c>min(Outbox:BackoffBaseSeconds × 2^(attempts−1),
/// Outbox:BackoffMaxMinutes)</c> (defaults 30 s and 60 min); a sweep only picks up messages whose
/// <see cref="OutboxMessage.NextAttemptUtc"/> has passed. The failure that brings attempts to
/// <c>Outbox:MaxAttempts</c> (default 15, about 8 hours of retrying) dead-letters the message and logs it
/// once at Error. A payload that can't be deserialized into its (resolvable) type is dead-lettered at
/// once, since retrying can't fix it. A type this build can't resolve is an ordinary failure with
/// backoff, not a dead letter: during an overlapping deploy an older instance meets event types only the
/// newer build knows. Either way the sweep carries on with the next message.</para>
///
/// <para><b>Ordering.</b> Sweeps run in occurrence order, but a message waiting out its backoff, or
/// dead-lettered, no longer holds back later messages for the same aggregate. That is safe because the
/// handlers check current state rather than assuming delivery order (the HR ones since Phase 2); a
/// future handler that needs strict order must do the same.</para>
///
/// <para><b>Recovery.</b> Fix the cause, then reset all three fields, or the next failure dead-letters
/// the message again: <c>SET dead_lettered_utc = NULL, attempts = 0, next_attempt_utc = now()</c>.</para>
/// </summary>
public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    OutboxSignal signal,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TimeSpan _fallbackInterval = TimeSpan.FromSeconds(
        int.TryParse(configuration["Outbox:FallbackPollSeconds"], out int seconds) ? seconds : 60);

    private readonly int _maxAttempts =
        int.TryParse(configuration["Outbox:MaxAttempts"], out int maxAttempts) && maxAttempts > 0 ? maxAttempts : 15;

    private readonly int _backoffBaseSeconds =
        int.TryParse(configuration["Outbox:BackoffBaseSeconds"], out int baseSeconds) && baseSeconds >= 0 ? baseSeconds : 30;

    private readonly int _backoffMaxMinutes =
        int.TryParse(configuration["Outbox:BackoffMaxMinutes"], out int maxMinutes) && maxMinutes >= 0 ? maxMinutes : 60;

    // E2-01: this instance's own ExecuteAsync loop and any caller driving ProcessPendingAsync directly
    // (tests' "flush now" pattern, see that method's own doc comment) are two independent entry points
    // into the SAME pending-message set, with no coordination between them. Without this gate, both can
    // fetch and attempt the SAME row concurrently: InboxDedupeDecorator's own transaction correctly stops
    // the double side-effect (the loser's insert hits PK_inbox_messages and its whole transaction rolls
    // back), but the loser's ProcessOneAsync swallows that exception and returns normally - so a test
    // calling ProcessPendingAsync can return having "lost" its own race, with zero guarantee the OTHER,
    // un-awaited, winning attempt (running on the BackgroundService's own task) has committed yet. A read
    // immediately after can then observe stale pre-commit state. Confirmed via repeated runs of
    // CrossModuleSmokeTests.Documents_flow_spans_all_five_modules_end_to_end_and_is_stable_across_two_runs
    // in true single-process isolation (rules out cross-test database pollution): PK_inbox_messages
    // violations recorded against DocumentUploadedIntegrationEvent, ApprovalDecidedIntegrationEvent, and
    // DocumentApprovedIntegrationEvent itself, in both passing and failing runs - the race exists either
    // way, it just doesn't always resolve before the reader gets there. A single-flight semaphore across
    // BOTH entry points removes the interleaving entirely: only one full sweep of pending messages runs
    // at a time, so "pending" is never read while another sweep already claimed the same rows.
    private readonly SemaphoreSlim _sweepGate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!BackgroundLoops.Enabled(configuration))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Whether this returns because of a real nudge or because the fallback timed out, the
                // next step is identical: go process whatever's pending now. OutboxSignal.WaitAsync
                // deliberately returns plain Task, not Task<bool>, so there's nothing to branch on here.
                await signal.WaitAsync(_fallbackInterval, stoppingToken);
                await ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Host shutting down - loop condition below ends the loop.
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox processing cycle failed");
            }
        }
    }

    /// <summary>Processes whatever is currently pending, once, without the wait loop. Public so tests
    /// (and a manual "flush now" trigger) can drive it directly.</summary>
    public async Task ProcessPendingAsync(CancellationToken ct)
    {
        // Single-flight across this instance - see _sweepGate's own remarks for why: this method and
        // ExecuteAsync's own loop are two independent callers of the same pending-message set.
        await _sweepGate.WaitAsync(ct);
        try
        {
            List<Guid> pendingIds;
            await using (AsyncServiceScope probeScope = scopeFactory.CreateAsyncScope())
            {
                SharedDbContext shared = probeScope.ServiceProvider.GetRequiredService<SharedDbContext>();
                DateTimeOffset now = timeProvider.GetUtcNow();
                pendingIds = await shared.OutboxMessages
                    .Where(m => m.ProcessedUtc == null && m.DeadLetteredUtc == null && m.NextAttemptUtc <= now)
                    .OrderBy(m => m.OccurredUtc)
                    .Select(m => m.Id)
                    .ToListAsync(ct);
            }

            foreach (Guid id in pendingIds)
            {
                await ProcessOneAsync(id, ct);
            }
        }
        finally
        {
            _sweepGate.Release();
        }
    }

    private async Task ProcessOneAsync(Guid messageId, CancellationToken ct)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        SharedDbContext shared = scope.ServiceProvider.GetRequiredService<SharedDbContext>();

        OutboxMessage? message = await shared.OutboxMessages.FirstOrDefaultAsync(m => m.Id == messageId, ct);
        if (message is null || message.ProcessedUtc is not null || message.DeadLetteredUtc is not null)
        {
            return; // already handled by a concurrent run
        }

        Type? eventType = ResolveType(message.Type);
        if (eventType is null)
        {
            // Not a dead letter: an older instance in an overlapping deploy can't resolve a type only
            // the newer build has. An ordinary failure, with backoff, until MaxAttempts.
            Fail(message, $"Unknown outbox event type '{message.Type}'.", exception: null);
            await shared.SaveChangesAsync(ct);
            return;
        }

        IIntegrationEvent? @event = Deserialize(message.Content, eventType, out string? payloadError);
        if (@event is null)
        {
            // Retrying can't fix a payload that doesn't fit its own type.
            DateTimeOffset now = timeProvider.GetUtcNow();
            string error = $"Payload could not be deserialized as '{eventType.Name}': {payloadError}";
            message.RecordFailure(error, now, TimeSpan.Zero);
            message.MarkDeadLettered(now, error);
            logger.LogError("Outbox message {MessageId} ({EventType}) dead-lettered after {Attempts} attempts: {Error}",
                message.Id, eventType.Name, message.Attempts, error);
            await shared.SaveChangesAsync(ct);
            return;
        }

        IEventBus eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        try
        {
            await eventBus.PublishAsync(@event, ct);
            message.MarkProcessed(timeProvider.GetUtcNow());
        }
        catch (Exception ex)
        {
            Fail(message, ex.Message, ex);
        }

        await shared.SaveChangesAsync(ct);
    }

    /// <summary>Records a failed attempt with backoff, or dead-letters the message when this was the
    /// last one allowed. Earlier failures log at Warning; the dead letter logs once, at Error.</summary>
    private void Fail(OutboxMessage message, string error, Exception? exception)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        message.RecordFailure(error, now, BackoffAfter(message.Attempts + 1));
        string typeName = ShortTypeName(message.Type);

        if (message.Attempts >= _maxAttempts)
        {
            message.MarkDeadLettered(now, error);
            logger.LogError(exception,
                "Outbox message {MessageId} ({EventType}) dead-lettered after {Attempts} attempts: {Error}",
                message.Id, typeName, message.Attempts, error);
            return;
        }

        logger.LogWarning(exception,
            "Outbox message {MessageId} ({EventType}) failed on attempt {Attempt}; next attempt at {NextAttemptUtc}",
            message.Id, typeName, message.Attempts, message.NextAttemptUtc);
    }

    /// <summary><c>min(base × 2^(attempt−1), max)</c>. Computed in seconds as a double and capped
    /// before converting, so a large attempt count can't overflow.</summary>
    private TimeSpan BackoffAfter(int attempt)
    {
        double seconds = _backoffBaseSeconds * Math.Pow(2, Math.Max(0, attempt - 1));
        return TimeSpan.FromSeconds(Math.Min(seconds, _backoffMaxMinutes * 60d));
    }

    private static Type? ResolveType(string typeName)
    {
        try
        {
            return Type.GetType(typeName, throwOnError: false);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or BadImageFormatException)
        {
            // A malformed name or an assembly that fails to load: still "can't resolve it here".
            return null;
        }
    }

    private static IIntegrationEvent? Deserialize(string content, Type eventType, out string? error)
    {
        try
        {
            error = null;
            if (JsonSerializer.Deserialize(content, eventType, JsonOptions) is IIntegrationEvent @event)
            {
                return @event;
            }

            error = "the payload is null or not an integration event";
            return null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>The type's own name from an assembly-qualified one, for logs - the message may carry a
    /// type this build can't load, so this doesn't resolve it.</summary>
    private static string ShortTypeName(string assemblyQualifiedName)
    {
        string fullName = assemblyQualifiedName.Split(',')[0];
        int dot = fullName.LastIndexOf('.');
        return dot < 0 ? fullName : fullName[(dot + 1)..];
    }
}
