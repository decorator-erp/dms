using BuildingBlocks;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sites.Contracts;
using Sites.Infrastructure.Persistence;

namespace Sites.Infrastructure;

/// <summary>
/// The seventh module. Registration order mirrors <c>CrmModule</c>/<c>DocumentsModule</c> exactly.
///
/// <para><c>AddModuleCqrs</c> and the scope decorator are deliberately absent until
/// <c>Sites.Application</c> has handlers to scan (S1-06) and <c>SiteScopeDecorator</c> exists (S1-05) —
/// the wiring checklist's step 4/7 split. Adding them now would register a decorator over an empty
/// handler set.</para>
/// </summary>
internal sealed class SitesModule : IModule
{
    public string Name => "Sites";

    public IServiceCollection Register(IServiceCollection services, IConfiguration cfg)
    {
        services.AddDbContext<SitesDbContext>(options =>
            options.UseNpgsql(cfg.GetConnectionString("DefaultConnection")));

        services.AddSingleton<IModulePermissions>(new ModulePermissionsSet(Name, SitesPermissions.All));

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapModuleEndpoints(typeof(SitesModule).Assembly);
}
