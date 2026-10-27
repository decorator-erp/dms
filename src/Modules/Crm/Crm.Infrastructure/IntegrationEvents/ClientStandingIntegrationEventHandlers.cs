using BuildingBlocks.Events;
using Crm.Contracts;
using Crm.Infrastructure.Persistence;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Crm.Infrastructure.IntegrationEvents;

/// <summary>
/// X-3: disables the portal login of a client that was deactivated or soft-deleted. Placed here, not in
/// Identity, for the same reason as <see cref="ClientCreatedIntegrationEventHandler"/>.
///
/// <para><b>The current state wins over the event</b>, as in HR's <c>EmployeeDeactivatedIntegrationEventHandler</c>
/// (HR-1): if the client was reactivated before this event was delivered, disabling the login now would undo
/// that reactivation, so the handler does nothing. <c>DeactivateAsync</c> is idempotent, so an outbox retry
/// re-applies nothing. It also revokes every refresh token and bumps the security stamp, so the client's live
/// sessions end at their next request.</para>
/// </summary>
internal sealed class ClientDeactivatedIntegrationEventHandler(IUserAdministration userAdministration, CrmDbContext db)
    : IIntegrationEventHandler<ClientDeactivatedIntegrationEvent>
{
    public async Task HandleAsync(ClientDeactivatedIntegrationEvent @event, CancellationToken ct)
    {
        bool? isActive = await ClientStanding.IsActiveAsync(db, @event.ClientId, ct);
        if (isActive is not false)
        {
            return;
        }

        Result result = await userAdministration.DeactivateAsync(@event.AppUserId, ct);
        if (result.IsFailure)
        {
            // Throw, so OutboxProcessor retries: a deactivated client must not keep a working login.
            throw new InvalidOperationException(
                $"Failed to deactivate the portal login for client '{@event.ClientId}': {result.Error.Description}");
        }
    }
}

/// <summary>X-3: re-enables the portal login of a client that was reactivated. The current state wins, as in
/// <see cref="ClientDeactivatedIntegrationEventHandler"/>: a client deactivated again before delivery stays
/// locked out.</summary>
internal sealed class ClientReactivatedIntegrationEventHandler(IUserAdministration userAdministration, CrmDbContext db)
    : IIntegrationEventHandler<ClientReactivatedIntegrationEvent>
{
    public async Task HandleAsync(ClientReactivatedIntegrationEvent @event, CancellationToken ct)
    {
        bool? isActive = await ClientStanding.IsActiveAsync(db, @event.ClientId, ct);
        if (isActive is not true)
        {
            return;
        }

        Result result = await userAdministration.ReactivateAsync(@event.AppUserId, ct);
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Failed to reactivate the portal login for client '{@event.ClientId}': {result.Error.Description}");
        }
    }
}

internal static class ClientStanding
{
    /// <summary>The client's active flag as committed now, or null when there is no such client.</summary>
    public static Task<bool?> IsActiveAsync(CrmDbContext db, Guid clientId, CancellationToken ct) =>
        db.Clients.AsNoTracking()
            .Where(c => c.Id == clientId)
            .Select(c => (bool?)c.IsActive)
            .FirstOrDefaultAsync(ct);
}
