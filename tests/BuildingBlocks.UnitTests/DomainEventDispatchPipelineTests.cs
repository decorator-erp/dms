using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BuildingBlocks.UnitTests;

/// <summary>
/// G0-03 verify, case 2: a command raising a domain event dispatches it after success and not after
/// failure. Also exercised through the real pipeline.
/// </summary>
public class DomainEventDispatchPipelineTests
{
    private static ServiceProvider BuildProvider(CallTracker tracker)
    {
        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddDbContext<TestDbContext>(o => o
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            // G5-08's command-path transaction fix (DomainEventDispatchDecorator) always opens an
            // explicit transaction; the InMemory provider has no real transactions and would otherwise
            // escalate that to an error. This test only cares whether the event dispatches after
            // success/not after failure, not about transactional isolation, so the no-op is fine here.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddModuleCqrs<TestDbContext>(typeof(DomainEventDispatchPipelineTests).Assembly);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SuccessfulCommand_DispatchesRaisedDomainEvent()
    {
        var tracker = new CallTracker();
        await using var provider = BuildProvider(tracker);
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RaiseEventCommand>>();

        var result = await handler.HandleAsync(new RaiseEventCommand(ShouldSucceed: true));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, tracker.DomainEventHandlerCalls);
    }

    [Fact]
    public async Task FailedCommand_DoesNotDispatchTheEventItRaised()
    {
        var tracker = new CallTracker();
        await using var provider = BuildProvider(tracker);
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RaiseEventCommand>>();

        var result = await handler.HandleAsync(new RaiseEventCommand(ShouldSucceed: false));

        Assert.True(result.IsFailure);
        Assert.Equal(0, tracker.DomainEventHandlerCalls);
    }
}
