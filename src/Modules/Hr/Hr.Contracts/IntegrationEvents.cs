using BuildingBlocks.Events;

namespace Hr.Contracts;

public sealed record EmployeeHiredIntegrationEvent(
    Guid EventId, Guid EmployeeId, string AppUserId, Guid? DepartmentId, DateTimeOffset OccurredAtUtc)
    : IIntegrationEvent;

/// <summary>
/// Published by <c>Hr.Application</c>'s deactivate-employee command, alongside the employee's own
/// <c>IsActive = false</c> write, in the same <c>SaveChangesAsync</c> (G2-04). An
/// <c>IIntegrationEventHandler&lt;EmployeeDeactivatedIntegrationEvent&gt;</c> living in
/// <c>Hr.Infrastructure</c> (never Identity — Identity is a sink and must not reference
/// <c>Hr.Contracts</c>) calls <c>IUserAdministration.DeactivateAsync</c> to disable the login. Routed
/// through the outbox rather than a bare synchronous call so a failed Identity call after the employee
/// row has committed still retries — see the G2-04 PROGRESS.md entry for the full reasoning.
/// </summary>
public sealed record EmployeeDeactivatedIntegrationEvent(Guid EventId, Guid EmployeeId, DateTimeOffset OccurredAtUtc)
    : IIntegrationEvent;
