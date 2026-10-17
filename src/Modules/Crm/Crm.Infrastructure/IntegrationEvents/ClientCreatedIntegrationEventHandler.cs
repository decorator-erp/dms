using BuildingBlocks.Events;
using Crm.Contracts;
using Identity.Contracts;
using SharedKernel;

namespace Crm.Infrastructure.IntegrationEvents;

/// <summary>
/// Activates the portal login <c>CreateClientCommandHandler</c> provisioned, now that the client row it
/// belongs to has committed. Lives in <c>Crm.Infrastructure</c> rather than Identity because it names
/// <c>Identity.Contracts</c>, and Identity is a sink that must never reference <c>Crm.Contracts</c> —
/// the same placement, for the same reason, as HR's <c>EmployeeHiredIntegrationEventHandler</c>.
///
/// <para><c>ReactivateAsync</c> is idempotent, so an outbox retry re-applies nothing.</para>
///
/// <para><b>This event is shared with a Notifications handler, and handlers run sequentially.</b>
/// <c>EventBus</c> invokes every handler for an event in DI-registration order and rethrows the first
/// failure, so handlers behind the failing one are skipped for that delivery. Activation is therefore
/// <b>retry-dependent, not guaranteed on first dispatch</b>. That is safe because
/// <c>InboxDedupeDecorator</c> records an inbox row per (event, handler), so a retry runs only the
/// handlers that had not yet succeeded — but a co-handler that throws on <i>every</i> attempt would
/// starve every handler behind it indefinitely. Today this one happens to run first, only because
/// Notifications is registered last in <c>Program.cs</c> for unrelated reasons; that ordering is
/// accidental and must not be relied on. HR's <c>HireActivationRetryTests</c> covers the retry path for
/// the identical shape on the employee side.</para>
/// </summary>
internal sealed class ClientCreatedIntegrationEventHandler(IUserAdministration userAdministration)
    : IIntegrationEventHandler<ClientCreatedIntegrationEvent>
{
    public async Task HandleAsync(ClientCreatedIntegrationEvent @event, CancellationToken ct)
    {
        Result result = await userAdministration.ReactivateAsync(@event.AppUserId, ct);
        if (result.IsFailure)
        {
            // Throw, so OutboxProcessor retries. Swallowing this would leave a client unable to reach
            // the portal, with nothing recording why.
            throw new InvalidOperationException(
                $"Failed to activate the portal login for client '{@event.ClientId}': {result.Error.Description}");
        }
    }
}
