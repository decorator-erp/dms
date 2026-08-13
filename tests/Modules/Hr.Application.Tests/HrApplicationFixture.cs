using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Xunit;

namespace Hr.Application.Tests;

/// <summary>
/// Boots BOTH modules' real DI graphs (<c>IdentityModule.Register</c> and <c>HrModule.Register</c>) plus
/// <c>SharedDbContext</c>/<c>AddOutbox()</c> against one Testcontainers Postgres — this is the first
/// fixture in the solution that needs two modules and the outbox wiring at once, since G2-04's
/// deactivate-employee flow genuinely crosses from HR into Identity via
/// <c>EmployeeDeactivatedIntegrationEvent</c> → <c>OutboxProcessor</c> → <c>IUserAdministration</c>.
/// </summary>
public sealed class HrApplicationFixture : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private ServiceProvider _provider = null!;

    public OutboxProcessor OutboxProcessor { get; private set; } = null!;
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17").Build();
        await _container.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _container.GetConnectionString(),
                ["JwtSettings:Key"] = "test-signing-key-at-least-32-characters-long-000000",
                ["JwtSettings:Issuer"] = "hr-application-tests",
                ["JwtSettings:Audience"] = "hr-application-tests-client",
                ["JwtSettings:ExpiryMinutes"] = "60",
                ["JwtSettings:RefreshTokenExpiryDays"] = "14",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());

        services.AddDbContext<SharedDbContext>(o => o.UseNpgsql(_container.GetConnectionString()));
        services.AddOutbox();

        var modules = ModuleDiscovery.DiscoverModules(
            System.Reflection.Assembly.Load("Identity.Infrastructure"),
            System.Reflection.Assembly.Load("Hr.Infrastructure"));

        foreach (var module in modules)
            module.Register(services, configuration);

        _provider = services.BuildServiceProvider();

        await using (var shared = new SharedDbContext(
            new DbContextOptionsBuilder<SharedDbContext>().UseNpgsql(_container.GetConnectionString()).Options))
        {
            await shared.Database.MigrateAsync();
        }

        // IdentityModuleDbContext is internal to Identity.Infrastructure, and this project (correctly)
        // has no InternalsVisibleTo grant from it (rule 3 forbids cross-module IVT) - resolved by
        // assembly-qualified name instead, same technique ApiFixture/Rule9 tests use.
        var identityContextType = Type.GetType(
            "Identity.Infrastructure.Persistence.IdentityModuleDbContext, Identity.Infrastructure")
            ?? throw new InvalidOperationException("Could not load IdentityModuleDbContext.");

        await using (var scope = _provider.CreateAsyncScope())
        {
            var identityDb = (DbContext)scope.ServiceProvider.GetRequiredService(identityContextType);
            await identityDb.Database.MigrateAsync();
        }

        await using (var scope = _provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<Hr.Infrastructure.Persistence.HrDbContext>()
                .Database.MigrateAsync();
        }

        // Seeds Identity's default roles ("employee" among them) via the public IModule.SeedAsync -
        // RegisterCommand needs an existing role name, and this project has no InternalsVisibleTo grant
        // from Identity.Domain/Infrastructure to construct a RoleManager<ApplicationRole> directly
        // (correctly - rule 3 forbids cross-module IVT). DMS_SEED_ADMIN_PASSWORD stays unset and
        // TestHostEnvironment reports "Development", so AdminUserSeeder skips rather than throwing.
        var identityModule = modules.Single(m => m.Name == "Identity");
        await using (var scope = _provider.CreateAsyncScope())
        {
            await identityModule.SeedAsync(scope.ServiceProvider);
        }

        OutboxProcessor = _provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<OutboxProcessor>()
            .Single();
    }

    public AsyncServiceScope CreateScope() => _provider.CreateAsyncScope();

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class HrApplicationCollection : ICollectionFixture<HrApplicationFixture>
{
    public const string Name = "HrApplication";
}

file sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "Hr.Application.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.NullFileProvider();
}
