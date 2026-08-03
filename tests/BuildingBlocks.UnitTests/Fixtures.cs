using BuildingBlocks.Messaging;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace BuildingBlocks.UnitTests;

internal sealed class CallTracker
{
    public bool CommandHandlerInvoked { get; set; }
    public int DomainEventHandlerCalls { get; set; }
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
