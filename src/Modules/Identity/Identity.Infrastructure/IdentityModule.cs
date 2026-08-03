using System.Reflection;
using BuildingBlocks;
using BuildingBlocks.Web;
using Identity.Contracts;
using Identity.Domain;
using Identity.Infrastructure.Authentication;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Persistence.Seeders;
using Identity.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure;

internal sealed class IdentityModule : IModule
{
    public string Name => "Identity";

    public IServiceCollection Register(IServiceCollection services, IConfiguration cfg)
    {
        services.AddDbContext<IdentityModuleDbContext>(options =>
            options.UseNpgsql(cfg.GetConnectionString("DefaultConnection")));

        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<IdentityModuleDbContext>()
            .AddDefaultTokenProviders();

        services
            .AddOptions<JwtOptions>()
            .Bind(cfg.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IJwtProvider, JwtProvider>();
        services.AddScoped<IEffectivePermissionService, EffectivePermissionService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IRoleService, RoleService>();

        services.AddSingleton<IModulePermissions>(new ModulePermissionsSet(
            Name,
            [
                IdentityPermissions.UsersRead,
                IdentityPermissions.RolesRead,
                IdentityPermissions.RolesManage,
                IdentityPermissions.PermissionsManage,
            ]));

        // Singleton: it only aggregates IEnumerable<IModulePermissions>, and every registration of that
        // (this module's own included) is itself a singleton - no scoped/DbContext dependency here.
        services.AddSingleton<IPermissionCatalog, PermissionCatalog>();

        services.AddModuleCqrs<IdentityModuleDbContext>(Assembly.Load("Identity.Application"));

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapModuleEndpoints(typeof(IdentityModule).Assembly);

    /// <summary>Order matters: roles before role claims (claims attach to a role that must already
    /// exist) before the admin user (it must join an already-claimed role).</summary>
    public async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var db = services.GetRequiredService<IdentityModuleDbContext>();
        var catalog = services.GetRequiredService<IPermissionCatalog>();
        var configuration = services.GetRequiredService<IConfiguration>();
        var environment = services.GetRequiredService<IHostEnvironment>();

        await RoleSeeder.SeedAsync(roleManager, ct);
        await RoleClaimSeeder.SeedAsync(roleManager, db, catalog, ct);
        await AdminUserSeeder.SeedAsync(userManager, roleManager, configuration, environment, ct);
    }
}
