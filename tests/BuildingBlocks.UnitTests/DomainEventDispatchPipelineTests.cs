using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
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
        services.AddDbContext<TestDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
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
