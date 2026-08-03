using BuildingBlocks.Events;

namespace Identity.Contracts;

public sealed record UserRegisteredIntegrationEvent(
    Guid EventId, string UserId, string Email, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;

public sealed record UserDeactivatedIntegrationEvent(
    Guid EventId, string UserId, DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
