using System.Reflection;
using BuildingBlocks;
using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Contracts;
using Notifications.Infrastructure.Notifications;
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

        // N1-04. SignalR needs no PackageReference: BuildingBlocks.Web carries
        // <FrameworkReference Include="Microsoft.AspNetCore.App" />, and SignalR is in that shared
        // framework. No backplane - see NotificationsHub for what that costs on a second instance.
        services.AddSignalR();
        services.AddScoped<INotificationService, NotificationService>();

        // N1-06. Without this, the handlers in IntegrationEvents/ are registered NOWHERE and every
        // event they handle is marked processed with zero handlers - silently, which is the failure
        // class this repo keeps writing tests against. Scans THIS assembly (Infrastructure), not
        // Application: integration-event handlers live beside the contracts they call, the same split
        // every other module documents on its own call.
        services.AddModuleIntegrationEvents<NotificationsDbContext>(typeof(NotificationsModule).Assembly);

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app)
    {
        // MapHub from inside the module rather than from Api: the hub is internal (rule 4), so only
        // this assembly can name it. What Api DOES own is the authentication scheme - see the
        // OnMessageReceived handler in Api.DI, which cannot live here because the module has no access
        // to the JwtBearer options and should not.
        app.MapHub<NotificationsHub>(NotificationsHub.Route);
        return app;
    }
}
