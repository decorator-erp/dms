using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Jobs;

public static class PeriodicJobRegistration
{
    /// <summary>
    /// Registers the single <see cref="PeriodicJobWorker"/> hosted service. Call once from the
    /// composition root, alongside <c>AddOutbox()</c>.
    ///
    /// <para>Registers no jobs itself — each module registers its own <see cref="IPeriodicJob"/>
    /// implementations in its own <c>Register</c>, with a Scrutor scan over its Application assembly.
    /// With zero jobs registered the worker is a clean no-op, which is what makes it safe to wire the
    /// host up here before any job exists.</para>
    ///
    /// <para>One job is a knowing exception: <c>UnlinkedUserReaperJob</c> is registered by the Api
    /// itself, because it reads three modules' contracts at once and belongs to no single module. See
    /// its own remarks. <see cref="PeriodicJobWorker"/> enumerates <see cref="IPeriodicJob"/> from the
    /// container and does not care which assembly a job came from, so nothing else changes.</para>
    /// </summary>
    public static IServiceCollection AddPeriodicJobs(this IServiceCollection services)
    {
        services.AddHostedService<PeriodicJobWorker>();
        return services;
    }
}
