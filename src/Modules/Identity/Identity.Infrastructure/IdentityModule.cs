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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Identity.Infrastructure;

internal sealed class IdentityModule : IModule
{
    public string Name => "Identity";

    public IServiceCollection Register(IServiceCollection services, IConfiguration cfg)
    {
        services.AddModuleDbContext<IdentityModuleDbContext>(cfg);

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
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IUserAdministration, UserAdministration>();
        services.AddScoped<IUserProvisioning, UserProvisioning>();
        services.AddScoped<IUserLifecycleMaintenance, UserLifecycleMaintenance>();

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
        RoleManager<ApplicationRole> roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
        UserManager<ApplicationUser> userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        IdentityModuleDbContext db = services.GetRequiredService<IdentityModuleDbContext>();
        IPermissionCatalog catalog = services.GetRequiredService<IPermissionCatalog>();
        IConfiguration configuration = services.GetRequiredService<IConfiguration>();
        IHostEnvironment environment = services.GetRequiredService<IHostEnvironment>();

        await RoleSeeder.SeedAsync(roleManager);
        await RoleClaimSeeder.SeedAsync(roleManager, db, catalog);
        await AdminUserSeeder.SeedAsync(userManager, roleManager, configuration, environment);
    }
}
