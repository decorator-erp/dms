using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Events;

/// <summary>
/// Resolves and invokes every registered <see cref="IIntegrationEventHandler{T}"/> for an event's
/// runtime type. Zero registered handlers is a successful, silent no-op — see the remarks on
/// <see cref="Outbox.OutboxProcessor"/> for why that matters (a published event nobody subscribes to
/// must not block forever).
///
/// A single shared registration is safe here — unlike <see cref="DomainEvents.DomainEventDispatcher{TContext}"/>,
/// this class touches no DbContext of its own. Per-module atomicity is the responsibility of each
/// handler's own <see cref="Outbox.InboxDedupeDecorator{TContext,TEvent}"/>, resolved along with the
/// handler itself.
/// </summary>
internal sealed class EventBus(IServiceProvider serviceProvider) : IEventBus
{
    public async Task PublishAsync(IIntegrationEvent @event, CancellationToken ct)
    {
        Type eventType = @event.GetType();
        Type handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
        MethodInfo handleMethod = handlerType.GetMethod(nameof(IIntegrationEventHandler<IIntegrationEvent>.HandleAsync))!;

        foreach (object? handler in serviceProvider.GetServices(handlerType))
        {
            if (handler is null)
            {
                continue;
            }

            try
            {
                await (Task)handleMethod.Invoke(handler, [@event, ct])!;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            }
        }
    }
}
