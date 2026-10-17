using BuildingBlocks.Events;
using Hr.Contracts;
using Identity.Contracts;
using SharedKernel;

namespace Hr.Infrastructure.IntegrationEvents;

/// <summary>
/// Activates the login <c>CreateEmployeeCommandHandler</c> provisioned, now that the employee row it
/// belongs to has committed. Lives in <c>Hr.Infrastructure</c> rather than Identity for the same reason
/// <see cref="EmployeeDeactivatedIntegrationEventHandler"/> does: HR → Identity is the allowed
/// direction, and Identity is a sink that must never reference <c>Hr.Contracts</c>.
///
/// <para><c>ReactivateAsync</c> is idempotent, so an outbox retry after a partial failure re-applies
/// nothing. The event already carries <c>AppUserId</c>, so unlike the deactivate handler this one needs
/// no read of the employee row.</para>
///
/// <para><b>This event is shared with a Notifications handler, and handlers run sequentially.</b>
/// <c>EventBus</c> invokes every handler for an event in DI-registration order and rethrows the first
/// failure, so handlers behind the failing one are skipped for that delivery. Activation is therefore
/// <b>retry-dependent, not guaranteed on first dispatch</b>: if a co-handler throws first, this runs on
/// a later outbox attempt instead. That is safe because <c>InboxDedupeDecorator</c> records an inbox row
/// per (event, handler), so a retry skips whichever handlers already succeeded — but a co-handler that
/// throws on <i>every</i> attempt would starve every handler registered behind it indefinitely. Today
/// this one happens to run first, only because Notifications is registered last in <c>Program.cs</c> for
/// unrelated reasons; that ordering is accidental and must not be relied on.
/// <c>EmployeeFlowTests</c> covers the retry path with a deliberately failing co-handler.</para>
/// </summary>
internal sealed class EmployeeHiredIntegrationEventHandler(IUserAdministration userAdministration)
    : IIntegrationEventHandler<EmployeeHiredIntegrationEvent>
{
    public async Task HandleAsync(EmployeeHiredIntegrationEvent @event, CancellationToken ct)
    {
        Result result = await userAdministration.ReactivateAsync(@event.AppUserId, ct);
        if (result.IsFailure)
        {
            // Throw, so OutboxProcessor retries. Swallowing this would leave a hired employee unable to
            // log in, with nothing recording why.
            throw new InvalidOperationException(
                $"Failed to activate login for employee '{@event.EmployeeId}': {result.Error.Description}");
        }
    }
}
