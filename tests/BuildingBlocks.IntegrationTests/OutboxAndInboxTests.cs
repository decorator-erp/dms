using BuildingBlocks.Events;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BuildingBlocks.IntegrationTests;

/// <summary>
/// G0-05 verify, against a real Postgres (Testcontainers) — InMemory can't demonstrate real
/// transaction/commit boundaries, and this task is specifically about those boundaries (R1, R2).
/// </summary>
[Collection(OutboxCollection.Name)]
public sealed class OutboxAndInboxTests(OutboxFixture fixture)
{
    private ServiceProvider BuildProvider(CallCounter counter, FailureController failure)
    {
        var services = new ServiceCollection();
        services.AddSingleton(counter);
        services.AddSingleton(failure);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        // OutboxProcessor reads Outbox:FallbackPollSeconds (W1) - every test here drives it directly via
        // ProcessPendingAsync, never through the wait loop, so an empty configuration (falling back to
        // the 60s default) is enough; nothing in this file depends on the actual fallback duration.
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddDbContext<SharedDbContext>(o => o.UseNpgsql(fixture.ConnectionString));
        services.AddDbContext<TestModuleDbContext>(o => o.UseNpgsql(fixture.ConnectionString));
        services.AddOutbox();
        services.AddSingleton<OutboxProcessor>();
        services.AddModuleIntegrationEvents<TestModuleDbContext>(typeof(TestEventHandler).Assembly);
        return services.BuildServiceProvider();
    }

    private SharedDbContext OpenShared() =>
        new(new DbContextOptionsBuilder<SharedDbContext>().UseNpgsql(fixture.ConnectionString).Options);

    private TestModuleDbContext OpenModule() =>
        new(new DbContextOptionsBuilder<TestModuleDbContext>().UseNpgsql(fixture.ConnectionString).Options);

    [Fact]
    public async Task PublishEvent_HandlerRunsOnce()
    {
        var counter = new CallCounter();
        await using ServiceProvider provider = BuildProvider(counter, new FailureController());
        var @event = new TestEvent(Guid.NewGuid(), DateTimeOffset.UtcNow);

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IEventBus>()
                .PublishAsync(@event, CancellationToken.None);
        }

        Assert.Equal(1, counter.Count);

        await using TestModuleDbContext verify = OpenModule();
        Assert.Equal(1, await verify.SideEffects.CountAsync(x => x.EventId == @event.EventId));
    }

    [Fact]
    public async Task PublishSameEventTwice_HandlerRunsOnce()
    {
        var counter = new CallCounter();
        await using ServiceProvider provider = BuildProvider(counter, new FailureController());
        var @event = new TestEvent(Guid.NewGuid(), DateTimeOffset.UtcNow);

        for (int i = 0; i < 2; i++)
        {
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IEventBus>()
                .PublishAsync(@event, CancellationToken.None);
        }

        Assert.Equal(1, counter.Count);

        await using TestModuleDbContext verify = OpenModule();
        Assert.Equal(1, await verify.SideEffects.CountAsync(x => x.EventId == @event.EventId));
    }

    /// <summary>
    /// R1's "process killed before returning" case. A literal process kill can't be scripted in an
    /// automated test, so this proves the guarantee the design actually rests on: the handler's
    /// business write and its inbox marker commit through ONE SaveChangesAsync call
    /// (<see cref="InboxDedupeDecorator{TContext,TEvent}"/>), so there is no window in which one exists
    /// without the other. Calling PublishAsync a second time — exactly what a real OutboxProcessor
    /// would do on its next poll, since nothing here ever advances outbox_messages.processed_utc, the
    /// same as if the process had died right after the first commit — must not repeat the effect.
    /// </summary>
    [Fact]
    public async Task HandlerCommitsOwnWrite_RetryWithOutboxStillUnprocessed_DoesNotRepeatSideEffect()
    {
        var counter = new CallCounter();
        await using ServiceProvider provider = BuildProvider(counter, new FailureController());
        var @event = new TestEvent(Guid.NewGuid(), DateTimeOffset.UtcNow);

        await using (AsyncServiceScope scope1 = provider.CreateAsyncScope())
        {
            await scope1.ServiceProvider.GetRequiredService<IEventBus>()
                .PublishAsync(@event, CancellationToken.None);
        }

        await using (AsyncServiceScope scope2 = provider.CreateAsyncScope())
        {
            await scope2.ServiceProvider.GetRequiredService<IEventBus>()
                .PublishAsync(@event, CancellationToken.None);
        }

        Assert.Equal(1, counter.Count);

        await using TestModuleDbContext verify = OpenModule();
        Assert.Equal(1, await verify.SideEffects.CountAsync(x => x.EventId == @event.EventId));
    }

    [Fact]
    public async Task HandlerThrows_RecordsAttemptAndError_RetrySucceeds()
    {
        var counter = new CallCounter();
        var failure = new FailureController { FailNextCall = true };
        await using ServiceProvider provider = BuildProvider(counter, failure);
        var @event = new TestEvent(Guid.NewGuid(), DateTimeOffset.UtcNow);

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            TestModuleDbContext db = scope.ServiceProvider.GetRequiredService<TestModuleDbContext>();
            db.Outbox().Enqueue(@event);
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<OutboxProcessor>()
                .ProcessPendingAsync(CancellationToken.None);
        }

        Assert.Equal(1, counter.Count);
        await using (SharedDbContext verify = OpenShared())
        {
            OutboxMessage message = await verify.OutboxMessages.SingleAsync(m => m.Id == @event.EventId);
            Assert.Null(message.ProcessedUtc);
            Assert.Equal(1, message.Attempts);
            Assert.NotNull(message.Error);
        }

        // FailNextCall was consumed (and reset) by the first, failing attempt — this one succeeds.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<OutboxProcessor>()
                .ProcessPendingAsync(CancellationToken.None);
        }

        Assert.Equal(2, counter.Count);
        await using (SharedDbContext verify = OpenShared())
        {
            OutboxMessage message = await verify.OutboxMessages.SingleAsync(m => m.Id == @event.EventId);
            Assert.NotNull(message.ProcessedUtc);
        }
    }

    [Fact]
    public async Task EventWithNoHandler_MarkedProcessedOnFirstPass()
    {
        await using ServiceProvider provider = BuildProvider(new CallCounter(), new FailureController());
        var @event = new UnhandledTestEvent(Guid.NewGuid(), DateTimeOffset.UtcNow);

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            TestModuleDbContext db = scope.ServiceProvider.GetRequiredService<TestModuleDbContext>();
            db.Outbox().Enqueue(@event);
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<OutboxProcessor>()
                .ProcessPendingAsync(CancellationToken.None);
        }

        await using SharedDbContext verify = OpenShared();
        OutboxMessage message = await verify.OutboxMessages.SingleAsync(m => m.Id == @event.EventId);
        Assert.NotNull(message.ProcessedUtc);
        Assert.Equal(0, message.Attempts);
    }
}
