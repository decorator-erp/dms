using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel;
using Xunit;

namespace BuildingBlocks.IntegrationTests;

/// <summary>
/// The command-path counterpart to F1 (see <see cref="DomainEventCascadeTests"/>): a gap flagged as a
/// deferred TODO during Phase 2's G5-02 (no cascade existed yet to prove it against) and fixed here at
/// G5-08, whose own gate-cascade verify text ("a 3-level gate cascade resolves in one command's
/// transaction") depends on it. Before the fix, <c>DomainEventDispatchDecorator&lt;TContext&gt;</c>
/// called the inner command handler (which does its own <c>SaveChangesAsync</c>) and THEN called
/// <c>DomainEventDispatcher.DispatchAsync</c> (which saves again, per iteration, up to 10 times) as
/// separate commits — a crash between them left the direct write committed with its cascade unresolved.
/// Fixed the same way F1 fixed the integration-event path: wrap the whole
/// handler-call-plus-dispatch sequence in one explicit transaction. Uses the PUBLIC
/// <see cref="ModuleCqrsRegistration.AddModuleCqrs{TContext}"/> entry point (the same one every real
/// module's <c>Register()</c> calls) rather than naming the internal decorator type directly, so this
/// test exercises the exact wiring production code uses.
/// </summary>
[Collection(OutboxCollection.Name)]
public sealed class CommandPathCascadeTests(OutboxFixture fixture)
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

        // Scans THIS test assembly: finds TriggerCascadeCommandHandler (ICommandHandler<>) and
        // Level1DomainEventHandler/Level2DomainEventHandler (IDomainEventHandler<>) together, wiring
        // Validation -> Logging -> DomainEventDispatch exactly as a real module's Register() would.
        services.AddModuleCqrs<TestModuleDbContext>(typeof(TriggerCascadeCommandHandler).Assembly);

        return services.BuildServiceProvider();
    }

    private TestModuleDbContext OpenModule() =>
        new(new DbContextOptionsBuilder<TestModuleDbContext>().UseNpgsql(fixture.ConnectionString).Options);

    [Fact]
    public async Task TwoLevelCascade_HandlerWriteAndBothCascadeStepsCommitTogether()
    {
        var counter = new CallCounter();
        await using ServiceProvider provider = BuildProvider(counter, new FailureController());
        var entityId = Guid.NewGuid();

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ICommandHandler<TriggerCascadeCommand> handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<TriggerCascadeCommand>>();
            Result result = await handler.HandleAsync(new TriggerCascadeCommand(entityId));
            Assert.True(result.IsSuccess);
        }

        // Level1 handler ran once (drain iteration 1), Level2 handler ran once (drain iteration 2 - the
        // Level2 event only exists because Level1's handler enqueued it via IDomainEventQueue).
        Assert.Equal(2, counter.Count);

        await using TestModuleDbContext verify = OpenModule();
        Assert.Equal(1, await verify.CascadingEntities.CountAsync(x => x.Id == entityId));
        Assert.Equal(1, await verify.SideEffects.CountAsync(
            x => x.EventId == entityId && x.HandlerName == nameof(Level1DomainEventHandler)));
        Assert.Equal(1, await verify.SideEffects.CountAsync(
            x => x.EventId == entityId && x.HandlerName == nameof(Level2DomainEventHandler)));
    }

    /// <summary>The scenario the transaction wrap exists for: Level2's handler throws mid-cascade, AFTER
    /// the command handler's own <c>SaveChangesAsync</c> AND Level1's own dispatch-loop iteration have
    /// already run. With the wrap intact, nothing survives - not the entity, not either cascade step -
    /// so a retry reruns the whole chain from zero.</summary>
    [Fact]
    public async Task MidCascadeFailure_RollsBackTheEntireChain_NotJustTheUnfinishedStep()
    {
        var counter = new CallCounter();
        var failure = new FailureController { FailNextCall = true };
        await using ServiceProvider provider = BuildProvider(counter, failure);
        var entityId = Guid.NewGuid();

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ICommandHandler<TriggerCascadeCommand> handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<TriggerCascadeCommand>>();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => handler.HandleAsync(new TriggerCascadeCommand(entityId)));
        }

        await using TestModuleDbContext verify = OpenModule();
        Assert.Equal(0, await verify.CascadingEntities.CountAsync(x => x.Id == entityId));
        Assert.Equal(0, await verify.SideEffects.CountAsync(x => x.EventId == entityId));

        // A clean retry (failure already consumed) reruns the whole chain from zero, not a partial
        // resume - exactly the same guarantee F1 proved for the integration-event path.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ICommandHandler<TriggerCascadeCommand> handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<TriggerCascadeCommand>>();
            Result result = await handler.HandleAsync(new TriggerCascadeCommand(entityId));
            Assert.True(result.IsSuccess);
        }

        await using TestModuleDbContext afterRetry = OpenModule();
        Assert.Equal(1, await afterRetry.CascadingEntities.CountAsync(x => x.Id == entityId));
        Assert.Equal(1, await afterRetry.SideEffects.CountAsync(
            x => x.EventId == entityId && x.HandlerName == nameof(Level1DomainEventHandler)));
        Assert.Equal(1, await afterRetry.SideEffects.CountAsync(
            x => x.EventId == entityId && x.HandlerName == nameof(Level2DomainEventHandler)));
    }
}
