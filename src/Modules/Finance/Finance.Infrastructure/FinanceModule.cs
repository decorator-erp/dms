using System.Reflection;
using BuildingBlocks;
using BuildingBlocks.Web;
using Finance.Contracts;
using Finance.Infrastructure.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.Infrastructure;

/// <summary>
/// The ninth and last business module.
///
/// <para><b>Grows with what it can actually serve</b>, following the precedent this repo set at N1-01.
/// F1-04 registered only the DbContext and the permission set, because scanning an empty
/// <c>Finance.Application</c> for handlers would have found zero types and succeeded silently — the
/// failure shape <c>SitesModule</c> documents on its own decorator registration, and the one that
/// shipped Projects with no row-level access control at G5-07. <c>AddModuleCqrs</c> arrives here at
/// F1-06 with the first fifteen handlers. Still absent: <c>AddModuleIntegrationEvents</c> and the
/// Scrutor scan that registers <c>BudgetHandler</c>, both at F1-08.</para>
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

        // Never a re-typed literal — FinancePermissions.All is the single list, and F1-09 adds its
        // Count to the running permission total rather than a hard-coded number.
        services.AddSingleton<IModulePermissions>(new ModulePermissionsSet(Name, FinancePermissions.All));

        // F1-06. Loaded by name, not by referencing a type: Finance.Infrastructure must not reference
        // Finance.Application (that would be circular -- Application already depends on its own
        // Infrastructure). Api.csproj carries the ProjectReference purely to get the DLL into the
        // output directory where this call can find it.
        var applicationAssembly = Assembly.Load("Finance.Application");
        services.AddModuleCqrs<FinanceDbContext>(applicationAssembly);

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapModuleEndpoints(typeof(FinanceModule).Assembly);
}
