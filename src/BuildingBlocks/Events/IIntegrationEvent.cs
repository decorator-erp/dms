namespace BuildingBlocks.Events;

public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAtUtc { get; }
}
