using System.Reflection;
using BuildingBlocks;
using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sites.Contracts;
using Sites.Infrastructure.Decorators;
using Sites.Infrastructure.Persistence;
using Sites.Infrastructure.Services;

namespace Sites.Infrastructure;

/// <summary>
/// The seventh module. Registration order mirrors <c>CrmModule</c>/<c>DocumentsModule</c> exactly.
///
/// <para><b>Decorator registration order is load-bearing.</b> <c>AddSiteScopeDecorator</c> must run
/// AFTER <c>AddModuleCqrs</c> — Scrutor's <c>Decorate</c> wraps in call order, so the scope check has to
/// be added last to end up outermost. A scope check inside the validation decorator would report on a
/// payload the caller was never entitled to have validated, and would log the request as handled.</para>
/// </summary>
internal sealed class SitesModule : IModule
{
    public string Name => "Sites";

    public IServiceCollection Register(IServiceCollection services, IConfiguration cfg)
    {
        services.AddModuleDbContext<SitesDbContext>(cfg);

        // Backs SiteSchemaCache. TryAdd-based, so calling it here is safe whether or not the host or
        // another module has already registered it.
        services.AddMemoryCache();

        // Submit and decide stamp the row and the integration event from one clock. TryAdd because this
        // is a host-level concern that another module may register first; TimeProvider.System is the
        // framework default, and injecting it rather than calling DateTime.UtcNow is what lets the
        // lifecycle handlers be tested against a fixed time.
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<ISiteSchemaCache, SiteSchemaCache>();
        services.AddScoped<ISiteModuleValidator, SiteModuleValidator>();
        services.AddScoped<ISiteReadQueries, SiteReadQueries>();
        services.AddScoped<ISiteProjectUsage, SiteProjectUsage>();
        services.AddScoped<ISiteDocumentReader, SiteDocumentReader>();

        services.AddSingleton<IModulePermissions>(new ModulePermissionsSet(Name, SitesPermissions.All));

        var applicationAssembly = Assembly.Load("Sites.Application");
        services.AddModuleCqrs<SitesDbContext>(applicationAssembly);

        // Must run AFTER AddModuleCqrs -- see the type-level note. Scanned against the CONTRACTS
        // assembly, where ISiteScoped messages actually live: scanning the Application assembly would
        // find zero types, decorate nothing, and succeed silently -- which is exactly how G5-07 shipped
        // Projects with no row-level access control at all.
        services.AddSiteScopeDecorator(typeof(ISiteScoped).Assembly);

        // N1-06: Sites' FIRST AddModuleIntegrationEvents call. SiteApprovedNotificationHandler
        // subscribes to Sites' OWN event, because the recipient it wants - the SUBMITTER - is
        // sites.sites.CreatedById, while the payload carries only the approver. Without this line the
        // handler is registered nowhere and the event is marked processed with zero handlers,
        // silently - the same failure shape the decorator comment above describes.
        services.AddModuleIntegrationEvents<SitesDbContext>(typeof(SitesModule).Assembly);

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapModuleEndpoints(typeof(SitesModule).Assembly);
}
