using BuildingBlocks.Messaging;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel;
using Xunit;

namespace BuildingBlocks.UnitTests;

/// <summary>
/// G0-03 verify, case 1: a command with a failing validator returns Result.Failure and the inner
/// handler is never invoked. Exercised through the real AddModuleCqrs&lt;TContext&gt; pipeline, not by
/// instantiating the decorator directly — this is what actually ships.
/// </summary>
public class ValidationDecoratorPipelineTests
{
    [Fact]
    public async Task FailingValidator_ShortCircuits_InnerHandlerNeverInvoked()
    {
        var tracker = new CallTracker();

        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddDbContext<TestDbContext>(o => o
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            // See DomainEventDispatchPipelineTests' own remarks on this (G5-08's command-path
            // transaction fix vs. the InMemory provider's lack of real transaction support).
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddScoped<IValidator<ValidatedCommand>, ValidatedCommandValidator>();
        services.AddModuleCqrs<TestDbContext>(typeof(ValidationDecoratorPipelineTests).Assembly);

        await using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        ICommandHandler<ValidatedCommand> handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ValidatedCommand>>();

        Result result = await handler.HandleAsync(new ValidatedCommand(string.Empty));

        Assert.True(result.IsFailure);
        Assert.False(tracker.CommandHandlerInvoked);
    }
}
