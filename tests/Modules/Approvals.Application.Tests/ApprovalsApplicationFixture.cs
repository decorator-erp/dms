using BuildingBlocks;
using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Xunit;

namespace Approvals.Application.Tests;

/// <summary>
/// Boots BOTH modules' real DI graphs (<c>IdentityModule.Register</c> and <c>ApprovalsModule.Register</c>)
/// plus <c>SharedDbContext</c>/<c>AddOutbox()</c> against one Testcontainers Postgres — same shape as
/// <c>HrApplicationFixture</c> (G2-04), needed for the identical reason: <c>approvals.approvals</c>'s
/// cross-schema FK to <c>identity.users</c> needs a real user row, and <c>IApprovalRequester</c>'s own
/// flow needs a real <c>ApprovalsDbContext</c>/outbox wired end to end.
/// </summary>
public sealed class ApprovalsApplicationFixture : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private ServiceProvider _provider = null!;

    public OutboxProcessor OutboxProcessor { get; private set; } = null!;
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder().WithImage("postgres:17").Build();
        await _container.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _container.GetConnectionString(),
                ["JwtSettings:Key"] = "test-signing-key-at-least-32-characters-long-000000",
                ["JwtSettings:Issuer"] = "approvals-application-tests",
                ["JwtSettings:Audience"] = "approvals-application-tests-client",
                ["JwtSettings:ExpiryMinutes"] = "60",
                ["JwtSettings:RefreshTokenExpiryDays"] = "14",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());

        services.AddScoped<TestCurrentUser>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<TestCurrentUser>());

        services.AddDbContext<SharedDbContext>(o => o.UseNpgsql(_container.GetConnectionString()));
        services.AddOutbox();

        var modules = ModuleDiscovery.DiscoverModules(
            System.Reflection.Assembly.Load("Identity.Infrastructure"),
            System.Reflection.Assembly.Load("Approvals.Infrastructure"));

        foreach (var module in modules)
            module.Register(services, configuration);

        _provider = services.BuildServiceProvider();

        await using (var shared = new SharedDbContext(
            new DbContextOptionsBuilder<SharedDbContext>().UseNpgsql(_container.GetConnectionString()).Options))
        {
            await shared.Database.MigrateAsync();
        }

        var identityContextType = Type.GetType(
            "Identity.Infrastructure.Persistence.IdentityModuleDbContext, Identity.Infrastructure")
            ?? throw new InvalidOperationException("Could not load IdentityModuleDbContext.");

        await using (var scope = _provider.CreateAsyncScope())
        {
            var identityDb = (DbContext)scope.ServiceProvider.GetRequiredService(identityContextType);
            await identityDb.Database.MigrateAsync();
        }

        var approvalsContextType = Type.GetType(
            "Approvals.Infrastructure.Persistence.ApprovalsDbContext, Approvals.Infrastructure")
            ?? throw new InvalidOperationException("Could not load ApprovalsDbContext.");

        await using (var scope = _provider.CreateAsyncScope())
        {
            var approvalsDb = (DbContext)scope.ServiceProvider.GetRequiredService(approvalsContextType);
            await approvalsDb.Database.MigrateAsync();
        }

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
public sealed class ApprovalsApplicationCollection : ICollectionFixture<ApprovalsApplicationFixture>
{
    public const string Name = "ApprovalsApplication";
}

file sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "Approvals.Application.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.NullFileProvider();
}
