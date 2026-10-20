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
///
/// <para>HR/Identity Phase 0 (Aug 0.1) adds the query side, which had no validating decorator at all,
/// and pins how validators execute: with the caller's cancellation token, and one at a time.</para>
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

    [Fact]
    public async Task FailingQueryValidator_ShortCircuits_InnerHandlerNeverInvoked()
    {
        var tracker = new CallTracker();
        await using ServiceProvider provider = BuildPipeline(tracker);
        using IServiceScope scope = provider.CreateScope();
        IQueryHandler<ValidatedQuery, int> handler =
            scope.ServiceProvider.GetRequiredService<IQueryHandler<ValidatedQuery, int>>();

        Result<int> result = await handler.HandleAsync(new ValidatedQuery(string.Empty));

        Assert.True(result.IsFailure);
        ValidationError error = Assert.IsType<ValidationError>(result.Error);
        Assert.Contains(error.Errors, e => e.Code == nameof(ValidatedQuery.Value));
        Assert.False(tracker.QueryHandlerInvoked);
    }

    [Fact]
    public async Task PassingQueryValidator_ReachesTheHandler()
    {
        var tracker = new CallTracker();
        await using ServiceProvider provider = BuildPipeline(tracker);
        using IServiceScope scope = provider.CreateScope();
        IQueryHandler<ValidatedQuery, int> handler =
            scope.ServiceProvider.GetRequiredService<IQueryHandler<ValidatedQuery, int>>();

        Result<int> result = await handler.HandleAsync(new ValidatedQuery("abc"));

        Assert.True(result.IsSuccess);
        Assert.True(tracker.QueryHandlerInvoked);
    }

    [Fact]
    public async Task Validators_ReceiveTheCallersCancellationToken()
    {
        var tracker = new CallTracker();
        await using ServiceProvider provider = BuildPipeline(tracker);
        using IServiceScope scope = provider.CreateScope();
        IQueryHandler<ObservedQuery, int> handler =
            scope.ServiceProvider.GetRequiredService<IQueryHandler<ObservedQuery, int>>();
        using var cts = new CancellationTokenSource();

        await handler.HandleAsync(new ObservedQuery(), cts.Token);

        Assert.Equal(2, tracker.ValidatorTokens.Count);
        Assert.All(tracker.ValidatorTokens, token => Assert.Equal(cts.Token, token));
    }

    [Fact]
    public async Task Validators_RunOneAtATime()
    {
        var tracker = new CallTracker();
        await using ServiceProvider provider = BuildPipeline(tracker);
        using IServiceScope scope = provider.CreateScope();
        IQueryHandler<ObservedQuery, int> handler =
            scope.ServiceProvider.GetRequiredService<IQueryHandler<ObservedQuery, int>>();

        await handler.HandleAsync(new ObservedQuery());

        // Both ran, never together: validators share the request's scoped DbContext.
        Assert.Equal(2, tracker.ValidatorTokens.Count);
        Assert.Equal(1, tracker.MaxValidatorsInFlight);
    }

    private static ServiceProvider BuildPipeline(CallTracker tracker)
    {
        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddDbContext<TestDbContext>(o => o
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddModuleCqrs<TestDbContext>(typeof(ValidationDecoratorPipelineTests).Assembly);
        return services.BuildServiceProvider();
    }
}
