using System.Reflection;
using BuildingBlocks;
using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Xunit;

namespace Sites.Application.Tests;

/// <summary>
/// Boots all seven modules' real DI graphs plus <c>SharedDbContext</c>/<c>AddOutbox()</c> against one
/// Testcontainers Postgres, migrated in <c>ModuleDependencyOrder.SchemaOrder</c> order.
///
/// <para><b>All seven, not just Sites.</b> Sites' cross-schema foreign keys point at projects, hr, crm
/// and documents, so those schemas must exist before the sites migration runs at all — a Sites-only
/// fixture cannot migrate. This is the shape <c>docs/adding-a-module.md</c> prescribes for a module
/// with cross-schema FKs.</para>
///
/// <para>Resolves handlers from DI rather than constructing them, which is the point: it proves the
/// <c>AddModuleCqrs</c> registration and the decorator chain, not just the handler bodies.</para>
/// </summary>
public sealed class SitesApplicationFixture : IAsyncLifetime
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
                ["JwtSettings:Issuer"] = "sites-application-tests",
                ["JwtSettings:Audience"] = "sites-application-tests-client",
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
            Assembly.Load("Identity.Infrastructure"),
            Assembly.Load("Hr.Infrastructure"),
            Assembly.Load("Approvals.Infrastructure"),
            Assembly.Load("Crm.Infrastructure"),
            Assembly.Load("Projects.Infrastructure"),
            Assembly.Load("Documents.Infrastructure"),
            Assembly.Load("Sites.Infrastructure"),
            // N1-06: Notifications is a REQUIRED module now, not an optional extra. Approvals,
            // Projects, Crm and Sites all register integration-event handlers that take
            // INotificationService, so a host that boots them without this one fails to resolve
            // the handler the moment an event is delivered.
            Assembly.Load("Notifications.Infrastructure"));

        foreach (var module in modules)
            module.Register(services, configuration);

        _provider = services.BuildServiceProvider();

        await using (var shared = new SharedDbContext(
            new DbContextOptionsBuilder<SharedDbContext>().UseNpgsql(_container.GetConnectionString()).Options))
        {
            await shared.Database.MigrateAsync();
        }

        await MigrateAsync("Identity.Infrastructure.Persistence.IdentityModuleDbContext, Identity.Infrastructure");
        await MigrateAsync("Hr.Infrastructure.Persistence.HrDbContext, Hr.Infrastructure");
        await MigrateAsync("Approvals.Infrastructure.Persistence.ApprovalsDbContext, Approvals.Infrastructure");
        await MigrateAsync("Crm.Infrastructure.Persistence.CrmDbContext, Crm.Infrastructure");
        await MigrateAsync("Projects.Infrastructure.Persistence.ProjectsDbContext, Projects.Infrastructure");
        await MigrateAsync("Documents.Infrastructure.Persistence.DocumentsDbContext, Documents.Infrastructure");
        await MigrateAsync("Sites.Infrastructure.Persistence.SitesDbContext, Sites.Infrastructure");
        // notify is LAST in SchemaOrder: its two foreign keys point at identity.users, and
        // nothing points into it.
        await MigrateAsync("Notifications.Infrastructure.Persistence.NotificationsDbContext, Notifications.Infrastructure");

        var identityModule = modules.Single(m => m.Name == "Identity");
        await using (var scope = _provider.CreateAsyncScope())
        {
            await identityModule.SeedAsync(scope.ServiceProvider);
        }

        OutboxProcessor = _provider.GetServices<IHostedService>().OfType<OutboxProcessor>().Single();
    }

    private async Task MigrateAsync(string assemblyQualifiedContextTypeName)
    {
        var contextType = Type.GetType(assemblyQualifiedContextTypeName)
            ?? throw new InvalidOperationException($"Could not load type '{assemblyQualifiedContextTypeName}'.");

        await using var scope = _provider.CreateAsyncScope();
        var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
        await context.Database.MigrateAsync();
    }

    public AsyncServiceScope CreateScope() => _provider.CreateAsyncScope();

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class SitesApplicationCollection : ICollectionFixture<SitesApplicationFixture>
{
    public const string Name = "SitesApplication";
}

file sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "Sites.Application.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.NullFileProvider();
}
