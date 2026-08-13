using BuildingBlocks.Events;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddHostedService<OutboxProcessor>();
        return services;
    }
}
