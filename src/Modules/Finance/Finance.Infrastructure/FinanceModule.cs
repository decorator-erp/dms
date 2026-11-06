using System.Reflection;
using BuildingBlocks;
using BuildingBlocks.Jobs;
using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using Finance.Contracts;
using Finance.Infrastructure.Persistence;
using Finance.Infrastructure.Services;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Projects.Contracts;

namespace Finance.Infrastructure;

/// <summary>
/// The ninth and last business module.
///
/// <para><b>Grew with what it could actually serve</b>, following the precedent this repo set at N1-01.
/// F1-04 registered only the DbContext and the permission set, because scanning an empty
/// <c>Finance.Application</c> for handlers would have found zero types and succeeded silently — the
/// failure shape <c>SitesModule</c> documents on its own decorator registration, and the one that
/// shipped Projects with no row-level access control at G5-07. <c>AddModuleCqrs</c> arrived at F1-06
/// with the first handlers; the two Scrutor scans and the outbox registration arrive here at F1-08 with
/// <c>BudgetHandler</c>, <c>InvoiceDueJob</c> and the seven integration events.</para>
///
/// <para><b>Not yet in <c>Program.cs</c>.</b> The composition root loads module assemblies by name from
/// an explicit list; Finance joins it at F1-09 when it has endpoints to mount. Until then this type is
/// discovered only by <c>ApiFixture.MigrateSchemaAsync</c>, which builds a throwaway
/// <c>ServiceCollection</c> through <see cref="Register"/> purely to resolve and migrate the DbContext.
/// That is why <c>Api.csproj</c> already references this project: <c>Integration.Tests</c> references
/// only <c>Api</c>, so a module whose DLL never reaches Api output cannot be
/// <c>Assembly.Load</c>ed there.</para>
///
/// <para><see cref="MapEndpoints"/> maps nothing today, but the scan is harmless and correct: it finds
/// no endpoint types until F1-09 adds them, at which point they mount without touching this file.</para>
/// </summary>
internal sealed class FinanceModule : IModule
{
    public string Name => "Finance";

    public IServiceCollection Register(IServiceCollection services, IConfiguration cfg)
    {
        services.AddModuleDbContext<FinanceDbContext>(cfg);

        // crm-complete E2/E3: Projects asks which stages/tasks have money attached before a hard delete.
        services.AddScoped<IFinanceProjectWorkUsage, FinanceProjectWorkUsage>();

        // Never a re-typed literal — FinancePermissions.All is the single list, and F1-09 adds its
        // Count to the running permission total rather than a hard-coded number.
        services.AddSingleton<IModulePermissions>(new ModulePermissionsSet(Name, FinancePermissions.All));

        // F1-06. Loaded by name, not by referencing a type: Finance.Infrastructure must not reference
        // Finance.Application (that would be circular -- Application already depends on its own
        // Infrastructure). Api.csproj carries the ProjectReference purely to get the DLL into the
        // output directory where this call can find it.
        var applicationAssembly = Assembly.Load("Finance.Application");
        services.AddModuleCqrs<FinanceDbContext>(applicationAssembly);

        // InvoiceDueJob stamps its notification from one clock. TryAdd because this is a host-level
        // concern another module may register first; injecting TimeProvider rather than calling
        // DateTime.UtcNow is what lets the job's tier logic be tested against a fixed time.
        services.TryAddSingleton(TimeProvider.System);

        // F1-08: BudgetHandler, the tenth and last ConditionType. Scanned rather than named because it
        // is `internal` in Finance.Application and Finance.Infrastructure has no InternalsVisibleTo
        // grant from it -- the same Scrutor technique Documents, CRM and Projects already use for the
        // same reason.
        services.Scan(scan => scan.FromAssemblies(applicationAssembly)
            .AddClasses(classes => classes.AssignableTo<IGateConditionTypeHandler>(), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime());

        // F1-08: InvoiceDueJob, the eighth IPeriodicJob, deferred from Phase 6 because it reads Invoices.
        services.Scan(scan => scan.FromAssemblies(applicationAssembly)
            .AddClasses(classes => classes.AssignableTo<IPeriodicJob>(), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime());

        // F1-08: Finance PUBLISHES seven integration events and SUBSCRIBES to none -- the scan finds no
        // handlers in this assembly today and that is correct, not an omission. The call is here so the
        // outbox plumbing for this DbContext is registered; the gate consequences of Finance's events
        // are consumed by Projects.Infrastructure, which is where those writes belong.
        services.AddModuleIntegrationEvents<FinanceDbContext>(typeof(FinanceModule).Assembly);

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapModuleEndpoints(typeof(FinanceModule).Assembly);
}
