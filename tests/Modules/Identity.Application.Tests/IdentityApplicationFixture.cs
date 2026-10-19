using BuildingBlocks;
using BuildingBlocks.Outbox;
using Identity.Contracts;
using Identity.Domain;
using Identity.Infrastructure.Authentication;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Identity.Application.Tests;

/// <summary>
/// Boots the same DI graph <see cref="Identity.Infrastructure.IdentityModule.Register"/> builds against
/// a real Postgres (Testcontainers) — including <c>AddModuleCqrs&lt;IdentityModuleDbContext&gt;</c> over
/// the real <c>Identity.Application</c> assembly, so handlers resolve wrapped in their actual
/// Validation → Logging → DomainEventDispatch chain, not a hand-wired substitute.
///
/// <para><see cref="SharedDbContext"/> is migrated first. The original reason was the register handler
/// staging an outbox row in the same <c>SaveChangesAsync</c> as the new user; that handler is deleted
/// and <c>UserProvisioning</c> enqueues nothing. It is still required, because <c>ApplicationUser</c> is
/// <c>IAuditable</c> and the interceptor writes to <c>shared.audit_logs</c> on every user write.</para>
/// </summary>
public sealed class IdentityApplicationFixture : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17").Build();
        await _container.StartAsync();

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Key"] = "test-signing-key-at-least-32-characters-long-000000",
                ["JwtSettings:Issuer"] = "identity-application-tests",
                ["JwtSettings:Audience"] = "identity-application-tests-client",
                ["JwtSettings:ExpiryMinutes"] = "60",
                ["JwtSettings:RefreshTokenExpiryDays"] = "14",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<IdentityModuleDbContext>(o => o.UseNpgsql(_container.GetConnectionString()));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<IdentityModuleDbContext>()
            .AddDefaultTokenProviders();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations();

        services.AddScoped<IJwtProvider, JwtProvider>();
        services.AddScoped<IEffectivePermissionService, EffectivePermissionService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IDelegationGuard, DelegationGuard>();
        services.AddScoped<IUserProvisioning, UserProvisioning>();
        services.AddScoped<IUserAdministration, UserAdministration>();

        // Mirrors IdentityModule.Register: a single fixed IModulePermissions registration (this module's
        // own set) is enough to exercise the catalog union logic without needing a second fake module.
        services.AddSingleton<IModulePermissions>(new ModulePermissionsSet(
            "Identity",
            [
                IdentityPermissions.UsersRead,
                IdentityPermissions.RolesRead,
                IdentityPermissions.RolesManage,
                IdentityPermissions.PermissionsManage,
            ]));
        services.AddSingleton<IPermissionCatalog, PermissionCatalog>();
        // Scoped through a settable slot so a test can act as somebody else (CreateScope(caller)). By
        // default the caller is CallerId: a real, active member of a seeded "admin" role holding the
        // whole catalog (seeded below). Since N1, role and permission writes check what the caller
        // effectively holds in the database, so a caller that exists only as an id holds nothing.
        services.AddScoped<CallerSlot>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CallerSlot>().Caller);

        services.AddModuleCqrs<IdentityModuleDbContext>(
            typeof(Identity.Application.Features.Auths.Login.LoginCommandHandler).Assembly);

        _provider = services.BuildServiceProvider();

        await using (var shared = new SharedDbContext(
            new DbContextOptionsBuilder<SharedDbContext>().UseNpgsql(_container.GetConnectionString()).Options))
        {
            await shared.Database.MigrateAsync();
        }

        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
        IdentityModuleDbContext db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();
        await db.Database.MigrateAsync();
        await SeedCallerAsync(scope.ServiceProvider, db);
    }

    /// <summary>The default caller's id: an active user in the seeded <c>admin</c> role.</summary>
    public const string CallerId = "test-caller-id";

    /// <summary>A scope whose <see cref="ICurrentUser"/> is <paramref name="caller"/> (default: <see cref="CallerId"/>).</summary>
    public AsyncServiceScope CreateScope(ICurrentUser? caller = null)
    {
        AsyncServiceScope scope = _provider.CreateAsyncScope();
        if (caller is not null)
        {
            scope.ServiceProvider.GetRequiredService<CallerSlot>().Caller = caller;
        }

        return scope;
    }

    private static async Task SeedCallerAsync(IServiceProvider sp, IdentityModuleDbContext db)
    {
        RoleManager<ApplicationRole> roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

        var admin = ApplicationRole.Create(Guid.NewGuid().ToString(), "admin");
        await roleManager.CreateAsync(admin);
        foreach (string permission in sp.GetRequiredService<IPermissionCatalog>().All().SelectMany(m => m.Permissions))
        {
            db.RoleClaims.Add(ApplicationRoleClaim.Create(admin.Id, BuildingBlocks.PermissionClaim.Type, permission));
        }

        await db.SaveChangesAsync();

        var caller = ApplicationUser.Create(
            CallerId, "test-caller@example.com", "test-caller@example.com", "Test", "Caller", UserType.Employee);
        await userManager.CreateAsync(caller, IdentityTestSeeding.Password);
        await userManager.AddToRoleAsync(caller, admin.Name!);
    }

    private sealed class CallerSlot
    {
        public ICurrentUser Caller { get; set; } = new TestCurrentUser(CallerId);
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class IdentityApplicationCollection : ICollectionFixture<IdentityApplicationFixture>
{
    public const string Name = "IdentityApplication";
}
