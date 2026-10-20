using BuildingBlocks.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Outbox;

public static class OutboxServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IEventBus"/>, the <see cref="OutboxSignal"/> singleton every module's
    /// commit-path decorators nudge, and the <see cref="OutboxProcessor"/> hosted service that waits on
    /// it. Call once from the composition root, alongside <c>AddDbContext&lt;SharedDbContext&gt;</c>
    /// (registered separately since it needs the connection string).
    /// </summary>
    public static IServiceCollection AddOutbox(this IServiceCollection services)
    {
        services.AddScoped<IEventBus, EventBus>();
        services.AddSingleton<OutboxSignal>();

        // OutboxProcessor's clock for due-checks and backoff. The same TryAdd as module registration, so
        // whichever runs first wins and a test's own TimeProvider registered earlier is kept.
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<OutboxProcessor>();
        return services;
    }
}
