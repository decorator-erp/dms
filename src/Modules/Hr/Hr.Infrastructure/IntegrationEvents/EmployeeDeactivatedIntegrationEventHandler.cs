using BuildingBlocks.Events;
using Hr.Contracts;
using Hr.Domain;
using Hr.Infrastructure.Persistence;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Hr.Infrastructure.IntegrationEvents;

/// <summary>
/// Lives in <c>Hr.Infrastructure</c>, not Identity — HR → Identity is the allowed direction, and
/// Identity must never reference <c>Hr.Contracts</c> (it is a sink). Calls
/// <see cref="IUserAdministration.DeactivateAsync"/>, which is idempotent, so <c>OutboxProcessor</c>
/// retrying this handler after a transient failure never double-applies anything. See
/// <c>DeactivateEmployeeCommandHandler</c> (G2-04, Hr.Application) for why this goes through the outbox
/// at all rather than a bare synchronous call.
/// </summary>
internal sealed class EmployeeDeactivatedIntegrationEventHandler(
    IUserAdministration userAdministration, HrDbContext db)
    : IIntegrationEventHandler<EmployeeDeactivatedIntegrationEvent>
{
    public async Task HandleAsync(EmployeeDeactivatedIntegrationEvent @event, CancellationToken ct)
    {
        // IgnoreQueryFilters: the employee this event is about is, by then, inactive.
        Employee? employee = await db.Employees.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == @event.EmployeeId, ct);
        if (employee is null)
        {
            return;
        }

        // HR-1: the current state wins over the event. If the employee was re-activated before this event
        // was delivered, disabling the login now would undo the activation; the activation's own event
        // will have re-enabled it (or will, in order).
        if (employee.IsActive)
        {
            return;
        }

        Result result = await userAdministration.DeactivateAsync(employee.AppUserId, ct);
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Failed to deactivate login for employee '{@event.EmployeeId}': {result.Error.Description}");
        }
    }
}
