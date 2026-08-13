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
/// unprocessed with attempts/error recorded, for the next poll to retry; a handler that already
/// committed its own effect (and its own inbox row, atomically — see
/// <see cref="InboxDedupeDecorator{TContext,TEvent}"/>) is skipped on that retry without redoing its
/// work.
/// </summary>
public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    OutboxSignal signal,
    IConfiguration configuration,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TimeSpan _fallbackInterval = TimeSpan.FromSeconds(
        int.TryParse(configuration["Outbox:FallbackPollSeconds"], out var seconds) ? seconds : 60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
        List<Guid> pendingIds;
        await using (var probeScope = scopeFactory.CreateAsyncScope())
        {
            var shared = probeScope.ServiceProvider.GetRequiredService<SharedDbContext>();
            pendingIds = await shared.OutboxMessages
                .Where(m => m.ProcessedUtc == null)
                .OrderBy(m => m.OccurredUtc)
                .Select(m => m.Id)
                .ToListAsync(ct);
        }

        foreach (var id in pendingIds)
            await ProcessOneAsync(id, ct);
    }

    private async Task ProcessOneAsync(Guid messageId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var shared = scope.ServiceProvider.GetRequiredService<SharedDbContext>();

        var message = await shared.OutboxMessages.FirstOrDefaultAsync(m => m.Id == messageId, ct);
        if (message is null || message.ProcessedUtc is not null)
            return; // already handled by a concurrent run

        var eventType = Type.GetType(message.Type)
            ?? throw new InvalidOperationException($"Unknown outbox event type '{message.Type}'.");
        var @event = (IIntegrationEvent)JsonSerializer.Deserialize(message.Content, eventType, JsonOptions)!;

        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        try
        {
            await eventBus.PublishAsync(@event, ct);
            message.MarkProcessed(DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Outbox message {MessageId} ({EventType}) failed on attempt {Attempt}",
                message.Id, eventType.Name, message.Attempts + 1);
            message.RecordFailure(ex.Message);
        }

        await shared.SaveChangesAsync(ct);
    }
}
