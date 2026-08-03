using System.Text.Json;
using BuildingBlocks.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Outbox;

/// <summary>
/// Polls <c>shared.outbox_messages</c> for unprocessed rows and publishes each through
/// <see cref="IEventBus"/>. Each message is re-fetched and processed in its own scope, so one
/// message's failure can't affect another's.
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
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox processing cycle failed");
            }
        }
        while (!stoppingToken.IsCancellationRequested
            && await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Processes whatever is currently pending, once, without the poll loop. Public so tests
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
