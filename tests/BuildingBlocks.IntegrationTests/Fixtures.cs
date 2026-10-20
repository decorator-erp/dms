using BuildingBlocks.Auditing;
using BuildingBlocks.Events;
using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

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

/// <summary>Stands in for a module's own DbContext: maps the shared outbox/inbox/audit tables without
/// owning their migration (see <see cref="OutboxModelConfiguration"/> and
/// <see cref="AuditModelConfiguration"/>), plus tables of its own.</summary>
public sealed class TestModuleDbContext(DbContextOptions<TestModuleDbContext> options) : DbContext(options)
{
    public DbSet<TestSideEffect> SideEffects => Set<TestSideEffect>();
    public DbSet<CascadingEntity> CascadingEntities => Set<CascadingEntity>();
    public DbSet<AuditedThing> AuditedThings => Set<AuditedThing>();
    public DbSet<OtherSchemaAuditedThing> OtherSchemaAuditedThings => Set<OtherSchemaAuditedThing>();
    public DbSet<UnauditedThing> UnauditedThings => Set<UnauditedThing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("test_module");
        modelBuilder.Entity<TestSideEffect>(b =>
        {
            b.ToTable("side_effects");
            b.HasKey(x => x.Id);
        });
        modelBuilder.Entity<CascadingEntity>(b =>
        {
            b.ToTable("cascading_entities");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Ignore(x => x.DomainEvents);
        });
        modelBuilder.Entity<AuditedThing>(b =>
        {
            b.ToTable("audited_things");
            b.HasKey(x => x.Id);
            b.ConfigureEntityConventions();
        });
        modelBuilder.Entity<OtherSchemaAuditedThing>(b =>
        {
            // Deliberately NOT the context's default schema. This is what makes the audit row's
            // Module field falsifiable: a Module read from the DbContext (or from a hand-maintained
            // entity-to-module dictionary, as the backend does) would say "test_module" here and be
            // wrong, while one read from the entity's own EF metadata says "test_module_other".
            b.ToTable("other_schema_audited_things", "test_module_other");
            b.HasKey(x => x.Id);
            b.ConfigureEntityConventions();
        });
        modelBuilder.Entity<UnauditedThing>(b =>
        {
            b.ToTable("unaudited_things");
            b.HasKey(x => x.Id);
            b.ConfigureEntityConventions();
        });
        modelBuilder.ConfigureOutbox(ownsMigration: false);
        modelBuilder.ConfigureAudit(ownsMigration: false);
    }
}

// --- N1-02 audit fixtures. Two auditable types, in two schemas, plus one deliberately unauditable
// type: with only one auditable type, an interceptor that hard-coded ANY single entity name and
// module would still pass, and the whole point of these tests is that both values are derived. ---

/// <summary>An auditable aggregate carrying one property redacted by NAME
/// (<c>PasswordHash</c>, which is how <c>ApplicationUser</c>'s inherited secrets are protected since
/// they cannot be attributed) and one redacted by <see cref="AuditIgnoreAttribute"/>.</summary>
public sealed class AuditedThing : Entity, IAuditable
{
    public string Label { get; private set; } = null!;

    /// <summary>Matched by <c>AuditRedaction.SensitiveNames</c>, no attribute involved.</summary>
    public string PasswordHash { get; private set; } = null!;

    [AuditIgnore]
    public string Confidential { get; private set; } = null!;

    private AuditedThing()
    {
        // EF materialization.
    }

    public AuditedThing(string label, string passwordHash, string confidential)
    {
        Label = label;
        PasswordHash = passwordHash;
        Confidential = confidential;
    }

    public void Rename(string label) => Label = label;
}

/// <summary>Identical in every way that matters except the schema it is mapped to.</summary>
public sealed class OtherSchemaAuditedThing : Entity, IAuditable
{
    public string Label { get; private set; } = null!;

    private OtherSchemaAuditedThing()
    {
        // EF materialization.
    }

    public OtherSchemaAuditedThing(string label) => Label = label;
}

/// <summary>Does NOT implement <see cref="IAuditable"/>. Proves the opt-in is the marker interface and
/// nothing else — writing one of these must produce no audit row at all.</summary>
public sealed class UnauditedThing : Entity
{
    public string Label { get; private set; } = null!;

    private UnauditedThing()
    {
        // EF materialization.
    }

    public UnauditedThing(string label) => Label = label;
}

// --- F1 fixtures: a two-level domain-event cascade triggered by an integration-event handler's write,
// exercising InboxDedupeDecorator's transaction wrap (see that class's own remarks for why it needs one). ---

/// <summary>An aggregate whose write raises a domain event — stands in for a real module entity like
/// <c>Approval</c> (G4-02) or <c>GateCondition</c> (G5-03), which do the same. Constructed with the
/// triggering integration event's own <c>EventId</c> as its <see cref="SharedKernel.Entity.Id"/> so a
/// test can correlate every row this chain produces back to one event, even when the shared
/// <see cref="OutboxFixture"/> container carries rows left over from other tests in the collection.</summary>
public sealed class CascadingEntity : Entity
{
    public CascadingEntity(Guid id) : base(id)
    {
    }

    public void RaiseLevel1() => AddDomainEvent(new Level1DomainEvent(Id));
}

/// <summary>Raised by <see cref="CascadingEntity.RaiseLevel1"/>; its handler enqueues
/// <see cref="Level2DomainEvent"/> via <see cref="IDomainEventQueue"/> rather than through a tracked
/// entity, exercising the queue's own drain path (the second of the dispatcher's two event sources) not
/// just the ChangeTracker one <see cref="CascadingEntity"/> already covers.</summary>
public sealed record Level1DomainEvent(Guid EntityId) : IDomainEvent;

/// <summary>Second-level cascade event — forces <c>DomainEventDispatcher.DispatchAsync</c> through a
/// second loop iteration (its own internal <c>SaveChangesAsync</c>), which is exactly the multi-commit
/// shape F1 must wrap in one transaction rather than leave as separate commits.</summary>
public sealed record Level2DomainEvent(Guid EntityId) : IDomainEvent;

public sealed record CascadeTestEvent(Guid EventId, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

/// <summary>
/// The integration-event handler InboxDedupeDecorator wraps directly. Stages the entity write that
/// raises the first domain event — does NOT call SaveChangesAsync itself, per the decorator's contract.
/// </summary>
public sealed class CascadeIntegrationEventHandler(TestModuleDbContext db) : IIntegrationEventHandler<CascadeTestEvent>
{
    public Task HandleAsync(CascadeTestEvent @event, CancellationToken ct)
    {
        var entity = new CascadingEntity(@event.EventId);
        entity.RaiseLevel1();
        db.CascadingEntities.Add(entity);
        return Task.CompletedTask;
    }
}

public sealed class Level1DomainEventHandler(TestModuleDbContext db, IDomainEventQueue queue, CallCounter counter)
    : IDomainEventHandler<Level1DomainEvent>
{
    public Task Handle(Level1DomainEvent domainEvent, CancellationToken cancellationToken)
    {
        counter.Count++;
        db.SideEffects.Add(new TestSideEffect(domainEvent.EntityId, nameof(Level1DomainEventHandler)));
        queue.Enqueue(new Level2DomainEvent(domainEvent.EntityId));
        return Task.CompletedTask;
    }
}

/// <summary>Reuses <see cref="FailureController"/> as the mid-cascade failure trigger for F1's
/// fault-injection test — throwing here happens AFTER the level-1 write's own dispatch-loop iteration has
/// already called <c>SaveChangesAsync</c> internally, which is exactly the scenario that distinguishes a
/// transaction-wrapped chain (nothing survives) from an unwrapped one (level-1 survives, level-2 and the
/// inbox row don't).</summary>
public sealed class Level2DomainEventHandler(TestModuleDbContext db, CallCounter counter, FailureController failure)
    : IDomainEventHandler<Level2DomainEvent>
{
    public Task Handle(Level2DomainEvent domainEvent, CancellationToken cancellationToken)
    {
        counter.Count++;

        if (failure.FailNextCall)
        {
            failure.FailNextCall = false;
            throw new InvalidOperationException("forced mid-cascade failure for the test");
        }

        db.SideEffects.Add(new TestSideEffect(domainEvent.EntityId, nameof(Level2DomainEventHandler)));
        return Task.CompletedTask;
    }
}

// --- Command-path cascade fixtures (G5-08's fix to DomainEventDispatchDecorator<TContext> — the
// command-path counterpart to F1). Reuses CascadingEntity/Level1DomainEvent/Level2DomainEvent and both
// their handlers above; only the trigger differs (a command, not an integration event). ---

public sealed record TriggerCascadeCommand(Guid EntityId) : ICommand;

/// <summary>Stages the entity write that raises the first domain event and calls its OWN
/// <c>SaveChangesAsync</c> — unlike <see cref="CascadeIntegrationEventHandler"/>, a real command handler
/// commits its own write directly (see e.g. <c>CompleteProjectStageCommandHandler</c>); the cascade that
/// follows is a SEPARATE commit unless <c>DomainEventDispatchDecorator</c> wraps both in one
/// transaction, which is exactly what this fixture proves.</summary>
public sealed class TriggerCascadeCommandHandler(TestModuleDbContext db) : ICommandHandler<TriggerCascadeCommand>
{
    public async Task<Result> HandleAsync(TriggerCascadeCommand command, CancellationToken ct = default)
    {
        var entity = new CascadingEntity(command.EntityId);
        entity.RaiseLevel1();
        db.CascadingEntities.Add(entity);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record TestEvent(Guid EventId, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

/// <summary>Toggle to make a handler fail exactly once, to test the "throws, then retry succeeds" path,
/// or on every call, to drive a message to the dead letter.</summary>
public sealed class FailureController
{
    public bool FailNextCall { get; set; }

    public bool AlwaysFail { get; set; }
}

public sealed class CallCounter
{
    public int Count { get; set; }
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

        if (failure.AlwaysFail)
        {
            throw new InvalidOperationException("forced failure on every call, for the test");
        }

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
