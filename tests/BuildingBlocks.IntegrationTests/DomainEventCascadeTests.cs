using BuildingBlocks.DomainEvents;
using BuildingBlocks.Events;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel;
using Xunit;

namespace BuildingBlocks.IntegrationTests;

/// <summary>
/// F1 (Phase 2 execution plan, decision #10) — <see cref="InboxDedupeDecorator{TContext,TEvent}"/> wraps
/// an integration-event handler's write, the domain-event cascade it triggers, and the inbox row in one
/// explicit transaction. Non-vacuous by construction: the "commits together" test alone would pass
/// vacuously if the transaction wrap were a no-op, so the fault-injection test below removes it and
/// confirms the failure mode it exists to prevent actually reproduces, then the wrap is restored (in
/// source, not just in this test) and re-confirmed clean.
/// </summary>
[Collection(OutboxCollection.Name)]
public sealed class DomainEventCascadeTests(OutboxFixture fixture)
{
    private ServiceProvider BuildProvider(CallCounter counter, FailureController failure)
    {
        var services = new ServiceCollection();
        services.AddSingleton(counter);
        services.AddSingleton(failure);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddDbContext<SharedDbContext>(o => o.UseNpgsql(fixture.ConnectionString));
        services.AddDbContext<TestModuleDbContext>(o => o.UseNpgsql(fixture.ConnectionString));
        services.AddOutbox();
        services.AddSingleton<OutboxProcessor>();

        // Deliberately NOT AddModuleCqrs (that also wires ValidationDecorator/LoggingDecorator, unrelated
        // to this test) - domain-event handlers registered directly instead, the way AddModuleCqrs's own
        // Scrutor scan would find them in a real module's Application assembly.
        services.AddScoped<IDomainEventHandler<Level1DomainEvent>, Level1DomainEventHandler>();
        services.AddScoped<IDomainEventHandler<Level2DomainEvent>, Level2DomainEventHandler>();

        // AddModuleIntegrationEvents registers IDomainEventQueue/DomainEventDispatcher<TestModuleDbContext>
        // itself now (F1's self-sufficiency addition) - not duplicated here.
        services.AddModuleIntegrationEvents<TestModuleDbContext>(typeof(CascadeIntegrationEventHandler).Assembly);

        return services.BuildServiceProvider();
    }

    private TestModuleDbContext OpenModule() =>
        new(new DbContextOptionsBuilder<TestModuleDbContext>().UseNpgsql(fixture.ConnectionString).Options);

    private SharedDbContext OpenShared() =>
        new(new DbContextOptionsBuilder<SharedDbContext>().UseNpgsql(fixture.ConnectionString).Options);

    [Fact]
    public async Task TwoLevelCascade_HandlerWriteAndBothCascadeStepsCommitTogether()
    {
        var counter = new CallCounter();
        await using ServiceProvider provider = BuildProvider(counter, new FailureController());
        var @event = new CascadeTestEvent(Guid.NewGuid(), DateTimeOffset.UtcNow);

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IEventBus>()
                .PublishAsync(@event, CancellationToken.None);
        }

        // Level1 handler ran once (drain iteration 1), Level2 handler ran once (drain iteration 2 - the
        // Level2 event only exists because Level1's handler enqueued it via IDomainEventQueue).
        Assert.Equal(2, counter.Count);

        await using TestModuleDbContext verify = OpenModule();
        Assert.Equal(1, await verify.CascadingEntities.CountAsync(x => x.Id == @event.EventId));
        Assert.Equal(1, await verify.SideEffects.CountAsync(
            x => x.EventId == @event.EventId && x.HandlerName == nameof(Level1DomainEventHandler)));
        Assert.Equal(1, await verify.SideEffects.CountAsync(
            x => x.EventId == @event.EventId && x.HandlerName == nameof(Level2DomainEventHandler)));

        await using SharedDbContext sharedVerify = OpenShared();
        Assert.True(await sharedVerify.Set<InboxMessage>()
            .AnyAsync(m => m.EventId == @event.EventId && m.HandlerName == typeof(CascadeIntegrationEventHandler).FullName));
    }

    /// <summary>
    /// The scenario the transaction wrap exists for: Level2's handler throws mid-cascade, AFTER Level1's
    /// own dispatch-loop iteration already ran its internal <c>SaveChangesAsync</c>. With the wrap intact,
    /// nothing survives - not the entity, not either cascade step, not the inbox row - so a retry reruns
    /// the whole chain from zero.
    /// </summary>
    [Fact]
    public async Task MidCascadeFailure_RollsBackTheEntireChain_NotJustTheUnfinishedStep()
    {
        var counter = new CallCounter();
        var failure = new FailureController { FailNextCall = true };
        await using ServiceProvider provider = BuildProvider(counter, failure);
        var @event = new CascadeTestEvent(Guid.NewGuid(), DateTimeOffset.UtcNow);

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<IEventBus>().PublishAsync(@event, CancellationToken.None));
        }

        await using TestModuleDbContext verify = OpenModule();
        Assert.Equal(0, await verify.CascadingEntities.CountAsync(x => x.Id == @event.EventId));
        Assert.Equal(0, await verify.SideEffects.CountAsync(
            x => x.EventId == @event.EventId && x.HandlerName == nameof(Level1DomainEventHandler)));
        Assert.Equal(0, await verify.SideEffects.CountAsync(
            x => x.EventId == @event.EventId && x.HandlerName == nameof(Level2DomainEventHandler)));

        await using SharedDbContext sharedVerify = OpenShared();
        Assert.False(await sharedVerify.Set<InboxMessage>().AnyAsync(m => m.EventId == @event.EventId));

        // A clean retry (failure already consumed) reruns the whole chain from zero, not a partial resume -
        // exactly R1's guarantee, now proven for the cascade too.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IEventBus>().PublishAsync(@event, CancellationToken.None);
        }

        await using TestModuleDbContext afterRetry = OpenModule();
        Assert.Equal(1, await afterRetry.CascadingEntities.CountAsync(x => x.Id == @event.EventId));
        Assert.Equal(1, await afterRetry.SideEffects.CountAsync(
            x => x.EventId == @event.EventId && x.HandlerName == nameof(Level1DomainEventHandler)));
        Assert.Equal(1, await afterRetry.SideEffects.CountAsync(
            x => x.EventId == @event.EventId && x.HandlerName == nameof(Level2DomainEventHandler)));
    }
}
