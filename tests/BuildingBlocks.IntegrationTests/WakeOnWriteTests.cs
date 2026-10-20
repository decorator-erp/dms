using System.Globalization;
using System.Text.Json;
using BuildingBlocks.Events;
using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SharedKernel;
using Xunit;

namespace BuildingBlocks.IntegrationTests;

/// <summary>
/// W1 verify: <see cref="OutboxProcessor"/> waits on <see cref="OutboxSignal"/> (wake-on-write) with a
/// configurable fallback, instead of a fixed 5s poll. Two tests, deliberately not one — each proves a
/// different half of the design and neither is sufficient alone:
/// <list type="bullet">
/// <item>the nudge path — delivery happens well inside a fallback window long enough that only the
/// nudge can explain it;</item>
/// <item>the fallback path — a row inserted directly via SQL, bypassing every decorator that would
/// nudge, is still delivered. This is what stops a later reader deleting the fallback as "redundant
/// now that wake-on-write exists."</item>
/// </list>
/// </summary>
[Collection(OutboxCollection.Name)]
public sealed class WakeOnWriteTests(OutboxFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public sealed record WakeTestEvent(Guid EventId, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

    public sealed record EnqueueWakeTestEventCommand(Guid EventId) : ICommand;

    /// <summary>Mirrors the shape every real command that publishes cross-module relies on: stage the
    /// outbox row, call its own SaveChangesAsync, and let DomainEventDispatchDecorator's transaction
    /// wrap + post-commit nudge (W1) do the rest — never nudge from inside the handler itself.</summary>
    public sealed class EnqueueWakeTestEventCommandHandler(TestModuleDbContext db)
        : ICommandHandler<EnqueueWakeTestEventCommand>
    {
        public async Task<Result> HandleAsync(EnqueueWakeTestEventCommand command, CancellationToken ct = default)
        {
            db.Outbox().Enqueue(new WakeTestEvent(command.EventId, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
    }

    public sealed class WakeTestEventHandler(TestModuleDbContext db, CallCounter counter)
        : IIntegrationEventHandler<WakeTestEvent>
    {
        public Task HandleAsync(WakeTestEvent @event, CancellationToken ct)
        {
            counter.Count++;
            db.SideEffects.Add(new TestSideEffect(@event.EventId, nameof(WakeTestEventHandler)));
            return Task.CompletedTask;
        }
    }

    private ServiceProvider BuildProvider(CallCounter counter, int fallbackSeconds)
    {
        var services = new ServiceCollection();
        services.AddSingleton(counter);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Outbox:FallbackPollSeconds"] = fallbackSeconds.ToString(CultureInfo.InvariantCulture),
            })
            .Build());
        services.AddDbContext<SharedDbContext>(o => o.UseNpgsql(fixture.ConnectionString));
        services.AddDbContext<TestModuleDbContext>(o => o.UseNpgsql(fixture.ConnectionString));
        services.AddOutbox();
        services.AddModuleCqrs<TestModuleDbContext>(typeof(EnqueueWakeTestEventCommandHandler).Assembly);
        services.AddModuleIntegrationEvents<TestModuleDbContext>(typeof(WakeTestEventHandler).Assembly);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Polls the durable row, not <see cref="CallCounter"/>. The counter increments synchronously
    /// inside the handler, before <see cref="InboxDedupeDecorator{TContext,TEvent}"/>'s own transaction
    /// commits and before <see cref="OutboxProcessor"/> marks the row processed - stopping the
    /// processor (which cancels its in-flight token) the moment the counter ticks up can race that
    /// commit and abort it, leaving <c>ProcessedUtc</c> null with no compensating decrement on the
    /// plain <c>int</c> counter. A later test's fresh processor would then find that same row still
    /// "pending", reprocess it for real, and increment ITS OWN counter - an apparent double-delivery
    /// that is actually a torn-down-too-early test, not a production bug. Waiting on the row itself
    /// means <c>StopAsync</c> only ever runs once nothing is in flight.
    /// </summary>
    private async Task<bool> WaitForProcessedAsync(Guid eventId, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            await using var shared = new SharedDbContext(
                new DbContextOptionsBuilder<SharedDbContext>().UseNpgsql(fixture.ConnectionString).Options);
            DateTimeOffset? processedUtc = await shared.OutboxMessages
                .Where(m => m.Id == eventId)
                .Select(m => m.ProcessedUtc)
                .SingleOrDefaultAsync();

            if (processedUtc is not null)
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }

    [Fact]
    public async Task Nudge_DeliversWellInsideAFallbackWindowLongEnoughOnlyTheNudgeExplainsIt()
    {
        var counter = new CallCounter();
        // 5 minutes: if delivery happens within the few-second wait below, the fallback poll (which
        // wouldn't fire for another ~300s) cannot be what explains it - only the nudge can.
        await using ServiceProvider provider = BuildProvider(counter, fallbackSeconds: 300);

        OutboxProcessor processor = provider.GetServices<IHostedService>().OfType<OutboxProcessor>().Single();
        await processor.StartAsync(CancellationToken.None);
        try
        {
            var eventId = Guid.NewGuid();
            await using (AsyncServiceScope scope = provider.CreateAsyncScope())
            {
                ICommandHandler<EnqueueWakeTestEventCommand> handler = scope.ServiceProvider
                    .GetRequiredService<ICommandHandler<EnqueueWakeTestEventCommand>>();
                Result result = await handler.HandleAsync(new EnqueueWakeTestEventCommand(eventId));
                Assert.True(result.IsSuccess);
            }

            bool delivered = await WaitForProcessedAsync(eventId, TimeSpan.FromSeconds(5));

            Assert.True(delivered, "Expected the nudge to deliver the event within 5s of a 300s fallback.");
            Assert.Equal(1, counter.Count);
        }
        finally
        {
            await processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Fallback_StillDeliversARowInsertedDirectlyViaSqlThatNeverNudged()
    {
        var counter = new CallCounter();
        // A short fallback the test can wait out without the run taking minutes.
        await using ServiceProvider provider = BuildProvider(counter, fallbackSeconds: 1);

        OutboxProcessor processor = provider.GetServices<IHostedService>().OfType<OutboxProcessor>().Single();
        await processor.StartAsync(CancellationToken.None);
        try
        {
            var @event = new WakeTestEvent(Guid.NewGuid(), DateTimeOffset.UtcNow);

            // Deliberately bypasses db.Outbox().Enqueue(...) and every decorator that would call
            // OutboxSignal.Nudge() - a raw INSERT is the one way to guarantee no nudge fires, proving
            // this delivery can only be explained by the fallback poll.
            await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
            {
                await connection.OpenAsync();
                await using NpgsqlCommand command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO shared.outbox_messages (id, type, content, occurred_utc, attempts)
                    VALUES (@id, @type, @content::jsonb, @occurred_utc, 0);
                    """;
                command.Parameters.AddWithValue("id", @event.EventId);
                command.Parameters.AddWithValue(
                    "type", typeof(WakeTestEvent).AssemblyQualifiedName!);
                command.Parameters.AddWithValue(
                    "content", JsonSerializer.Serialize(@event, JsonOptions));
                command.Parameters.AddWithValue("occurred_utc", @event.OccurredAtUtc);
                await command.ExecuteNonQueryAsync();
            }

            bool delivered = await WaitForProcessedAsync(@event.EventId, TimeSpan.FromSeconds(5));

            Assert.True(delivered, "Expected the 1s fallback poll to still deliver a row with no nudge.");
            Assert.Equal(1, counter.Count);
        }
        finally
        {
            await processor.StopAsync(CancellationToken.None);
        }
    }
}
