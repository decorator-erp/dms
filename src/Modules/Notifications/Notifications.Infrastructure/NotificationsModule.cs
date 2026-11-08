using System.Reflection;
using BuildingBlocks;
using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Contracts;
using Notifications.Infrastructure.Activities;
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

        // S11: the activity writer. Its cross-module callers reach it only through
        // ActivityRecordedIntegrationEvent on their own outbox (see IActivityRecorder).
        services.AddScoped<IActivityRecorder, ActivityRecorder>();

        // N1-06. Without this, the handlers in IntegrationEvents/ are registered NOWHERE and every
        // event they handle is marked processed with zero handlers - silently, which is the failure
        // class this repo keeps writing tests against. Scans THIS assembly (Infrastructure), not
        // Application: integration-event handlers live beside the contracts they call, the same split
        // every other module documents on its own call.
        services.AddModuleIntegrationEvents<NotificationsDbContext>(typeof(NotificationsModule).Assembly);

        // N1-07, and the ORDER of the two scans above and below is the thing to read carefully.
        // This project has lost three separate bugs to registration mistakes:
        //
        //   G0-03  Scrutor re-wrapped an open generic, decorating handlers twice.
        //   G7-07  a shared event type was double-wrapped when two modules both scanned for it.
        //   G5-07  a scan pointed at the WRONG ASSEMBLY, found zero types, and silently decorated
        //          nothing -- shipping a module with no row-level access control at all.
        //
        // G5-07 is the one that bites here, because the two scans target DIFFERENT assemblies and
        // neither throws when it matches nothing:
        //
        //   AddModuleCqrs               -> Notifications.APPLICATION  (command/query handlers)
        //   AddModuleIntegrationEvents  -> Notifications.INFRASTRUCTURE (event handlers, which call
        //                                  other modules' contracts and so live beside them)
        //
        // Swap the two assembly arguments and everything still compiles, still boots, and serves
        // 404s and un-notified events forever.
        services.AddModuleCqrs<NotificationsDbContext>(Assembly.Load("Notifications.Application"));

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app)
    {
        // MapHub from inside the module rather than from Api: the hub is internal (rule 4), so only
        // this assembly can name it. What Api DOES own is the authentication scheme - see the
        // OnMessageReceived handler in Api.DI, which cannot live here because the module has no access
        // to the JwtBearer options and should not.
        app.MapHub<NotificationsHub>(NotificationsHub.Route);
        return app.MapModuleEndpoints(typeof(NotificationsModule).Assembly);
    }
}
