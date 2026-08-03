namespace SharedKernel;

/// <summary>
/// Drains domain events from the EF ChangeTracker and the scoped <see cref="IDomainEventQueue"/>,
/// then invokes their handlers. Called at the unit-of-work / transaction boundary so events raised by
/// set-based writes (ExecuteUpdate/ExecuteDelete) fire even though they bypass the change tracker.
/// </summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(CancellationToken ct = default);
}
