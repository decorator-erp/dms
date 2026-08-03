namespace SharedKernel;

/// <summary>
/// Scoped (per unit-of-work) collector for domain events raised by set-based operations that do not load
/// an aggregate instance. The dispatcher drains this alongside the EF ChangeTracker on SaveChanges.
/// </summary>
public interface IDomainEventQueue
{
    void Enqueue(IDomainEvent domainEvent);

    /// <summary>Returns all queued events and clears the queue.</summary>
    IReadOnlyList<IDomainEvent> DrainAll();
}
