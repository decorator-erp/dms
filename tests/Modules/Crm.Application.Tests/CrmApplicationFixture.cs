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

namespace Crm.Application.Tests;

/// <summary>
/// Boots all six modules' real DI graphs plus <c>SharedDbContext</c>/<c>AddOutbox()</c> against one
/// Testcontainers Postgres — the full set this phase's own module graph ends up needing, brought
/// forward from G9-12 rather than grown one module at a time: Documents joins here (not later) because
/// <c>LinkCrmArtifactDocumentCommandHandler</c> needs a real <c>Documents.Contracts.IDocumentQueries</c>
/// to prove BOTH the known-document success path and the unknown-document rejection this task's own
/// verify requires — a fixture with no <c>IDocumentQueries</c> registered at all would fail every test
/// at DI resolution, not just the negative one. Migrated in <c>ModuleDependencyOrder.SchemaOrder</c>
/// order.
/// </summary>
public sealed class CrmApplicationFixture : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private ServiceProvider _provider = null!;

    public OutboxProcessor OutboxProcessor { get; private set; } = null!;
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17").Build();
        await _container.StartAsync();

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _container.GetConnectionString(),
                ["JwtSettings:Key"] = "test-signing-key-at-least-32-characters-long-000000",
                ["JwtSettings:Issuer"] = "crm-application-tests",
                ["JwtSettings:Audience"] = "crm-application-tests-client",
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

        IReadOnlyList<IModule> modules = ModuleDiscovery.DiscoverModules(
            Assembly.Load("Identity.Infrastructure"),
            Assembly.Load("Hr.Infrastructure"),
            Assembly.Load("Approvals.Infrastructure"),
            Assembly.Load("Crm.Infrastructure"),
            Assembly.Load("Projects.Infrastructure"),
            Assembly.Load("Documents.Infrastructure"),
            // N1-06: Notifications is a REQUIRED module now, not an optional extra. Approvals,
            // Projects, Crm and Sites all register integration-event handlers that take
            // INotificationService, so a host that boots them without this one fails to resolve
            // the handler the moment an event is delivered.
            Assembly.Load("Notifications.Infrastructure"));

        foreach (IModule module in modules)
        {
            module.Register(services, configuration);
        }

        _provider = services.BuildServiceProvider();

        await using (var shared = new SharedDbContext(
            new DbContextOptionsBuilder<SharedDbContext>().UseNpgsql(_container.GetConnectionString()).Options))
        {
            await shared.Database.MigrateAsync();
        }

        // ModuleDependencyOrder.SchemaOrder order: shared, identity, hr, approvals, crm, projects, documents.
        await MigrateAsync("Identity.Infrastructure.Persistence.IdentityModuleDbContext, Identity.Infrastructure");
        await MigrateAsync("Hr.Infrastructure.Persistence.HrDbContext, Hr.Infrastructure");
        await MigrateAsync("Approvals.Infrastructure.Persistence.ApprovalsDbContext, Approvals.Infrastructure");
        await MigrateAsync("Crm.Infrastructure.Persistence.CrmDbContext, Crm.Infrastructure");
        await MigrateAsync("Projects.Infrastructure.Persistence.ProjectsDbContext, Projects.Infrastructure");
        await MigrateAsync("Documents.Infrastructure.Persistence.DocumentsDbContext, Documents.Infrastructure");
        // notify is LAST in SchemaOrder: its two foreign keys point at identity.users, and
        // nothing points into it.
        await MigrateAsync("Notifications.Infrastructure.Persistence.NotificationsDbContext, Notifications.Infrastructure");

        IModule identityModule = modules.Single(m => m.Name == "Identity");
        await using (AsyncServiceScope scope = _provider.CreateAsyncScope())
        {
            await identityModule.SeedAsync(scope.ServiceProvider);
        }

        OutboxProcessor = _provider.GetServices<IHostedService>().OfType<OutboxProcessor>().Single();
    }

    private async Task MigrateAsync(string assemblyQualifiedContextTypeName)
    {
        Type contextType = Type.GetType(assemblyQualifiedContextTypeName)
            ?? throw new InvalidOperationException($"Could not load type '{assemblyQualifiedContextTypeName}'.");

        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
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
public sealed class CrmApplicationCollection : ICollectionFixture<CrmApplicationFixture>
{
    public const string Name = "CrmApplication";
}

file sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "Crm.Application.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.NullFileProvider();
}
