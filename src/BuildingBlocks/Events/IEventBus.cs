namespace BuildingBlocks.Events;

public interface IEventBus
{
    Task PublishAsync(IIntegrationEvent @event, CancellationToken ct);
}
