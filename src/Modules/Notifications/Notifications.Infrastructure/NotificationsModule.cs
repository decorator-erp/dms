using BuildingBlocks;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Contracts;
using Notifications.Infrastructure.Persistence;

namespace Notifications.Infrastructure;

/// <summary>
/// The eighth module.
///
/// <para><b>Created minimal at N1-03, then filled in over three more tasks.</b> That is not laziness —
/// <c>ApiFixture.MigrateModuleSchemaAsync</c> and <c>Rule9CrossSchemaForeignKeyTests</c> both migrate a
/// schema by calling <c>ModuleDiscovery.DiscoverModules(...).Single().Register(...)</c> on a throwaway
/// <c>ServiceCollection</c>, so the <c>notify</c> schema cannot migrate at all until this type exists.
/// <c>ProjectsModule</c> set the precedent (PROGRESS.md:1739: "created here — minimal, DbContext +
/// permissions only — not deferred to a later task").</para>
///
/// <para>What each task adds, and why then:</para>
/// <list type="bullet">
///   <item>N1-03 (here): <c>AddModuleDbContext</c> + <c>IModulePermissions</c>. Nothing else.</item>
///   <item>N1-04: <c>AddSignalR()</c>, <c>INotificationService</c>, and the hub mapping.</item>
///   <item>N1-06: <c>AddModuleIntegrationEvents</c>, once handlers land here.</item>
///   <item>N1-07: <c>AddModuleCqrs</c> and the endpoints.</item>
/// </list>
///
/// <para><b>Not yet in <c>Program.cs</c>.</b> The assembly joins the host's module list at N1-07, when
/// there are endpoints to serve. Until then the fixtures load it by name for migration only — which
/// works because <c>Api.csproj</c> already references it (N1-01), so the DLL reaches the output
/// directory.</para>
/// </summary>
internal sealed class NotificationsModule : IModule
{
    public string Name => "Notifications";

    public IServiceCollection Register(IServiceCollection services, IConfiguration cfg)
    {
        services.AddModuleDbContext<NotificationsDbContext>(cfg);
        services.AddSingleton<IModulePermissions>(new ModulePermissionsSet(Name, NotificationsPermissions.All));

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app) => app;
}
