using BuildingBlocks.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks;

public static class ModuleDbContextRegistration
{
    /// <summary>
    /// Registers a module's <see cref="DbContext"/> against the shared connection string, with the
    /// audit interceptor attached. Replaces the bare
    /// <c>AddDbContext&lt;T&gt;(o =&gt; o.UseNpgsql(cfg.GetConnectionString("DefaultConnection")))</c>
    /// that every module's <c>Register</c> used to spell out.
    ///
    /// <para>One extension rather than eight edited call sites, for one reason: the interceptor has to
    /// be attached identically on all of them or the audit trail has holes shaped like whichever module
    /// was missed, and nothing about eight separate lambdas enforces that they stay the same.</para>
    ///
    /// <para><b>The interceptor is a singleton, and that is load-bearing rather than tidy.</b> With the
    /// <c>(sp, options)</c> overload, <c>DbContextOptions&lt;T&gt;</c> is built per resolution. If
    /// <c>AddInterceptors</c> received a different instance each time, the <c>CoreOptionsExtension</c>
    /// service-provider hash would change per scope and EF Core would build — and cache — a new
    /// internal service provider for each one, which is the "more than 20 IServiceProvider instances
    /// have been created" leak. A singleton keeps the options hash stable. It is safe as one because
    /// its only dependencies are <see cref="IAuditContextAccessor"/> (backed by the singleton
    /// <c>IHttpContextAccessor</c> over <c>AsyncLocal</c>) and <see cref="TimeProvider"/>.</para>
    ///
    /// <para><b>The <c>TryAdd</c> on the accessor is an ordering contract.</b> <c>Program.cs</c> runs
    /// <c>AddApi</c> before the module loop, so <c>Api</c>'s HTTP-backed accessor is already registered
    /// and these calls no-op. Reverse that order and audit silently loses every actor — which is why
    /// <c>AuditContextAccessorRegistrationTests</c> asserts the real host resolves the HTTP one.</para>
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services,
        IConfiguration cfg)
        where TContext : DbContext
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAuditContextAccessor, NullAuditContextAccessor>();
        services.TryAddSingleton<AuditSaveChangesInterceptor>();

        services.AddDbContext<TContext>((sp, options) => options
            .UseNpgsql(cfg.GetConnectionString("DefaultConnection"))
            .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>()));

        return services;
    }
}
