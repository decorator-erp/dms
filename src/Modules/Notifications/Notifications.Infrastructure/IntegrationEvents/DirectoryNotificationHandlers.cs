using BuildingBlocks.Events;
using Crm.Contracts;
using Hr.Contracts;
using Notifications.Contracts;

namespace Notifications.Infrastructure.IntegrationEvents;

/// <summary>
/// <b>Handlers that live HERE because every recipient is derivable from the event payload alone.</b>
///
/// <para>That is the placement rule this phase adopted, and it is what keeps the boundary honest: a
/// notification handler belongs in <c>Notifications.Infrastructure</c> iff it needs nothing but the
/// payload plus already-published contracts. The moment it needs a table — a stage's team head, an
/// approval's approvers, a site's submitter — it moves to the module that owns that table, rather than
/// that module growing a read contract for one caller.</para>
///
/// <para>Both events below carry <c>AppUserId</c> outright, so these two need no lookup at all.</para>
/// </summary>
internal sealed class EmployeeHiredNotificationHandler(INotificationService notifications)
    : IIntegrationEventHandler<EmployeeHiredIntegrationEvent>
{
    public Task HandleAsync(EmployeeHiredIntegrationEvent @event, CancellationToken ct) =>
        notifications.NotifyAsync(
            @event.AppUserId,
            NotificationType.UserWelcome,
            title: "Welcome",
            body: "Your employee record has been created. You now have access to the workspace.",
            dedupeKey: $"employee-{@event.EmployeeId}-hired",
            ct: ct);
}

internal sealed class ClientCreatedNotificationHandler(INotificationService notifications)
    : IIntegrationEventHandler<ClientCreatedIntegrationEvent>
{
    public Task HandleAsync(ClientCreatedIntegrationEvent @event, CancellationToken ct) =>
        notifications.NotifyAsync(
            @event.AppUserId,
            NotificationType.UserWelcome,
            title: "Welcome",
            body: "Your client account is active. You can now follow your projects here.",
            dedupeKey: $"client-{@event.ClientId}-created",
            ct: ct);
}
