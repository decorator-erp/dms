using BuildingBlocks.Events;
using Hr.Contracts;
using Hr.Domain;
using Hr.Infrastructure.Persistence;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Hr.Infrastructure.IntegrationEvents;

/// <summary>
/// HR-1 (Aug 2.6): re-enables the login of a re-activated employee, the mirror of
/// <see cref="EmployeeDeactivatedIntegrationEventHandler"/>. Lives here for the same reason that one does
/// (HR → Identity through <c>Identity.Contracts</c>). <c>ReactivateAsync</c> is idempotent, so an outbox
/// retry is safe.
///
/// <para>The current state wins: if the employee was deactivated again before this event arrived, the
/// login stays disabled. Each handler checks the row, so deliveries in any order end where the employee
/// actually is.</para>
/// </summary>
internal sealed class EmployeeActivatedIntegrationEventHandler(IUserAdministration userAdministration, HrDbContext db)
    : IIntegrationEventHandler<EmployeeActivatedIntegrationEvent>
{
    public async Task HandleAsync(EmployeeActivatedIntegrationEvent @event, CancellationToken ct)
    {
        // IgnoreQueryFilters: the employee may have been deactivated again since; that is checked below.
        Employee? employee = await db.Employees.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == @event.EmployeeId, ct);
        if (employee is null || !employee.IsActive)
        {
            return;
        }

        Result result = await userAdministration.ReactivateAsync(employee.AppUserId, ct);
        if (result.IsFailure)
        {
            // Throw, so OutboxProcessor retries; swallowing it would leave a re-activated employee
            // locked out with nothing recording why.
            throw new InvalidOperationException(
                $"Failed to re-enable the login of employee '{@event.EmployeeId}': {result.Error.Description}");
        }
    }
}
