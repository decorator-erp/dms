namespace BuildingBlocks.Events;

public interface IIntegrationEventHandler<in T> where T : IIntegrationEvent
{
    Task HandleAsync(T @event, CancellationToken ct);
}
