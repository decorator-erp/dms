using BuildingBlocks.Events;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.IntegrationTests;

/// <summary>Stands in for "a module's own business row" — proves the handler's write and the inbox
/// marker commit together, without depending on a real module that doesn't exist yet.</summary>
public sealed class TestSideEffect
{
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public string HandlerName { get; private set; } = null!;

    private TestSideEffect()
    {
        // EF materialization.
    }

    public TestSideEffect(Guid eventId, string handlerName)
    {
        Id = Guid.NewGuid();
        EventId = eventId;
        HandlerName = handlerName;
    }
}

/// <summary>Stands in for a module's own DbContext: maps the shared outbox/inbox tables without
/// owning their migration (see <see cref="OutboxModelConfiguration"/>), plus one table of its own.</summary>
public sealed class TestModuleDbContext(DbContextOptions<TestModuleDbContext> options) : DbContext(options)
{
    public DbSet<TestSideEffect> SideEffects => Set<TestSideEffect>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("test_module");
        modelBuilder.Entity<TestSideEffect>(b =>
        {
            b.ToTable("side_effects");
            b.HasKey(x => x.Id);
        });
        modelBuilder.ConfigureOutbox(ownsMigration: false);
    }
}

public sealed record TestEvent(Guid EventId, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

/// <summary>Toggle to make a handler fail exactly once, to test the "throws, then retry succeeds" path.</summary>
public sealed class FailureController
{
    public bool FailNextCall { get; set; }
}

public sealed class CallCounter
{
    public int Count;
}

/// <summary>
/// Stages a business row on its own module context but deliberately does NOT call SaveChangesAsync
/// itself — per the contract InboxDedupeDecorator relies on, its own save at the end is the one commit
/// point for both this row and the inbox marker.
/// </summary>
public sealed class TestEventHandler(TestModuleDbContext db, CallCounter counter, FailureController failure)
    : IIntegrationEventHandler<TestEvent>
{
    public Task HandleAsync(TestEvent @event, CancellationToken ct)
    {
        counter.Count++;

        if (failure.FailNextCall)
        {
            failure.FailNextCall = false;
            throw new InvalidOperationException("forced failure for the test");
        }

        db.SideEffects.Add(new TestSideEffect(@event.EventId, nameof(TestEventHandler)));
        return Task.CompletedTask;
    }
}

/// <summary>An event type with no handler at all — exercises the zero-handler path.</summary>
public sealed record UnhandledTestEvent(Guid EventId, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
