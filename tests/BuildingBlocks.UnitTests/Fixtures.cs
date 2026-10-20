using BuildingBlocks.Messaging;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace BuildingBlocks.UnitTests;

internal sealed class CallTracker
{
    public bool CommandHandlerInvoked { get; set; }
    public bool QueryHandlerInvoked { get; set; }
    public int DomainEventHandlerCalls { get; set; }

    // Validator observations (ValidationDecoratorPipelineTests).
    public List<CancellationToken> ValidatorTokens { get; } = [];
    public int ValidatorsInFlight { get; set; }
    public int MaxValidatorsInFlight { get; set; }
}

internal sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<TestEntity> Entities => Set<TestEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<TestEntity>().Ignore(e => e.DomainEvents);
}

internal sealed class TestEntity : Entity
{
    public void RaiseTestEvent() => AddDomainEvent(new TestDomainEvent());
}

internal sealed record TestDomainEvent : IDomainEvent;

internal sealed class TestDomainEventHandler(CallTracker tracker) : IDomainEventHandler<TestDomainEvent>
{
    public Task Handle(TestDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        tracker.DomainEventHandlerCalls++;
        return Task.CompletedTask;
    }
}

// --- Validation short-circuit fixtures ---

internal sealed record ValidatedCommand(string Value) : ICommand;

internal sealed class ValidatedCommandValidator : AbstractValidator<ValidatedCommand>
{
    public ValidatedCommandValidator() => RuleFor(x => x.Value).NotEmpty();
}

internal sealed class ValidatedCommandHandler(CallTracker tracker) : ICommandHandler<ValidatedCommand>
{
    public Task<Result> HandleAsync(ValidatedCommand command, CancellationToken ct = default)
    {
        tracker.CommandHandlerInvoked = true;
        return Task.FromResult(Result.Success());
    }
}

internal sealed record ValidatedQuery(string Value) : IQuery<int>;

internal sealed class ValidatedQueryValidator : AbstractValidator<ValidatedQuery>
{
    public ValidatedQueryValidator() => RuleFor(x => x.Value).NotEmpty();
}

internal sealed class ValidatedQueryHandler(CallTracker tracker) : IQueryHandler<ValidatedQuery, int>
{
    public Task<Result<int>> HandleAsync(ValidatedQuery query, CancellationToken ct = default)
    {
        tracker.QueryHandlerInvoked = true;
        return Task.FromResult(Result.Success(query.Value.Length));
    }
}

// --- Validator execution fixtures: two async validators on one query, each recording the token it was
// handed and how many validators were running at once. The delay makes the second validator start while
// the first is still awaiting if the decorator ever runs them concurrently again.

internal sealed record ObservedQuery : IQuery<int>;

internal sealed class FirstObservingValidator : AbstractValidator<ObservedQuery>
{
    public FirstObservingValidator(CallTracker tracker) =>
        RuleFor(x => x).MustAsync((_, ct) => ObservingValidation.ObserveAsync(tracker, ct));
}

internal sealed class SecondObservingValidator : AbstractValidator<ObservedQuery>
{
    public SecondObservingValidator(CallTracker tracker) =>
        RuleFor(x => x).MustAsync((_, ct) => ObservingValidation.ObserveAsync(tracker, ct));
}

internal static class ObservingValidation
{
    public static async Task<bool> ObserveAsync(CallTracker tracker, CancellationToken ct)
    {
        tracker.ValidatorTokens.Add(ct);
        tracker.ValidatorsInFlight++;
        tracker.MaxValidatorsInFlight = Math.Max(tracker.MaxValidatorsInFlight, tracker.ValidatorsInFlight);
        await Task.Delay(TimeSpan.FromMilliseconds(50), CancellationToken.None);
        tracker.ValidatorsInFlight--;
        return true;
    }
}

internal sealed class ObservedQueryHandler : IQueryHandler<ObservedQuery, int>
{
    public Task<Result<int>> HandleAsync(ObservedQuery query, CancellationToken ct = default) =>
        Task.FromResult(Result.Success(0));
}

// --- Domain event dispatch fixtures ---

internal sealed record RaiseEventCommand(bool ShouldSucceed) : ICommand;

internal sealed class RaiseEventCommandHandler(TestDbContext db) : ICommandHandler<RaiseEventCommand>
{
    public async Task<Result> HandleAsync(RaiseEventCommand command, CancellationToken ct = default)
    {
        var entity = new TestEntity();
        entity.RaiseTestEvent();
        db.Entities.Add(entity);
        await db.SaveChangesAsync(ct);

        return command.ShouldSucceed ? Result.Success() : Error.Unexpected("forced failure for the test");
    }
}
