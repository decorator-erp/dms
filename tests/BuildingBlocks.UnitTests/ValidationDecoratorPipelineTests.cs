using BuildingBlocks.Messaging;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
        services.AddDbContext<TestDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddScoped<IValidator<ValidatedCommand>, ValidatedCommandValidator>();
        services.AddModuleCqrs<TestDbContext>(typeof(ValidationDecoratorPipelineTests).Assembly);

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ValidatedCommand>>();

        var result = await handler.HandleAsync(new ValidatedCommand(string.Empty));

        Assert.True(result.IsFailure);
        Assert.False(tracker.CommandHandlerInvoked);
    }
}
