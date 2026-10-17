using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Persistence.Seeders;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Xunit;

namespace Identity.Infrastructure.Tests;

/// <summary>
/// G1-07's verify block: "seeding twice produces the same rows." Runs <c>IdentityModule.Register</c> +
/// <c>IdentityModule.SeedAsync</c> against a real migrated Postgres, twice in a row, and asserts role,
/// role-claim, and user counts are identical after both runs — proving <c>RoleSeeder</c>,
/// <c>RoleClaimSeeder</c>, and <c>AdminUserSeeder</c> are each idempotent, not just individually
/// reasoned to be. Uses its own container rather than the shared <see cref="IdentityInfrastructureFixture"/>
/// collection, since it calls the module's own <c>Register</c> a second time on a fresh
/// <see cref="ServiceProvider"/> each round — isolating that from the other fixture's already-built one
/// avoids any doubt about which registration produced which row.
/// </summary>
public sealed class SeedingIdempotencyTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17").Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task Seeding_twice_produces_the_same_rows()
    {
        IModule module = ModuleDiscovery.DiscoverModules(typeof(IdentityModule).Assembly).Single();

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _container.GetConnectionString(),
                ["JwtSettings:Key"] = "test-signing-key-at-least-32-characters-long-000000",
                ["JwtSettings:Issuer"] = "identity-infrastructure-tests",
                ["JwtSettings:Audience"] = "identity-infrastructure-tests-client",
                ["JwtSettings:ExpiryMinutes"] = "60",
                ["JwtSettings:RefreshTokenExpiryDays"] = "14",
                ["DMS_SEED_ADMIN_PASSWORD"] = "SeedTest-P@ssw0rd123!",
            })
            .Build();

        // shared FIRST, then identity - ModuleDependencyOrder.SchemaOrder's own order, which this test
        // previously got away with skipping.
        //
        // N1-02 made it load-bearing: ApplicationRole, ApplicationRoleClaim and ApplicationUser are all
        // IAuditable, so seeding now stages rows into shared.audit_logs through the same SaveChanges,
        // and a database with only the identity schema fails with 42P01. That is the audit design
        // working as intended rather than a test needing an accommodation - shared is first in
        // SchemaOrder precisely so the table exists before any module writes - but this fixture built
        // its own database and so had to be told. Every *.Application.Tests fixture already did this.
        await using (var shared = new SharedDbContext(
            new DbContextOptionsBuilder<SharedDbContext>().UseNpgsql(_container.GetConnectionString()).Options))
        {
            await shared.Database.MigrateAsync();
        }

        await using (ServiceProvider migrateServices = BuildProvider(module, configuration))
        {
            using IServiceScope scope = migrateServices.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>().Database.MigrateAsync();
        }

        (int roles1, int claims1, int users1) = await SeedOnceAsync(module, configuration);
        (int roles2, int claims2, int users2) = await SeedOnceAsync(module, configuration);

        Assert.Equal(roles1, roles2);
        Assert.Equal(claims1, claims2);
        Assert.Equal(users1, users2);

        // Not just "unchanged" - the actual expected shape: DefaultRoles.All.Length roles (asserted
        // against the constant, not a literal, so this can never silently drift from it again - the
        // same fix shape as CrmSmokeTests' Assert.Equal(CrmPermissions.All.Count, ...)), the two
        // full-access roles carrying every catalog permission, exactly one seeded admin user.
        Assert.Equal(DefaultRoles.All.Length, roles1);
        Assert.Equal(1, users1);
    }

    private static async Task<(int Roles, int RoleClaims, int Users)> SeedOnceAsync(
        IModule module, IConfiguration configuration)
    {
        await using ServiceProvider services = BuildProvider(module, configuration);
        using IServiceScope scope = services.CreateScope();

        await module.SeedAsync(scope.ServiceProvider);

        IdentityModuleDbContext db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();
        return (await db.Roles.CountAsync(), await db.RoleClaims.CountAsync(), await db.Users.CountAsync());
    }

    private static ServiceProvider BuildProvider(IModule module, IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton(configuration);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment("Development"));
        module.Register(services, configuration);
        return services.BuildServiceProvider();
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Identity.Infrastructure.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
