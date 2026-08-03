using SharedKernel;

namespace BuildingBlocks.DomainEvents;

/// <summary>Scoped, in-memory implementation of <see cref="IDomainEventQueue"/>.</summary>
internal sealed class DomainEventQueue : IDomainEventQueue
{
    private readonly List<IDomainEvent> _events = [];

    public void Enqueue(IDomainEvent domainEvent) => _events.Add(domainEvent);

    public IReadOnlyList<IDomainEvent> DrainAll()
    {
        if (_events.Count == 0)
            return [];

        var drained = _events.ToList();
        _events.Clear();
        return drained;
    }
}
