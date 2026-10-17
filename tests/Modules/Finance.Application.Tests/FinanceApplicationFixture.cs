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

namespace Finance.Application.Tests;

/// <summary>
/// Boots all nine modules' real DI graphs plus <c>SharedDbContext</c>/<c>AddOutbox()</c> against one
/// Testcontainers Postgres, migrated in <c>ModuleDependencyOrder.SchemaOrder</c> order.
///
/// <para><b>All nine, not just Finance.</b> Finance's 22 cross-schema foreign keys point at projects,
/// crm and hr, so those schemas must exist before the finance migration runs at all — a Finance-only
/// fixture cannot migrate. This is the shape <c>docs/adding-a-module.md</c> prescribes for a module with
/// cross-schema FKs, and the ninth module is simply the largest instance of it so far.</para>
///
/// <para>Resolves handlers from DI rather than constructing them, which is the point: it proves the
/// <c>AddModuleCqrs</c> registration added at F1-06 and the decorator chain, not just the handler
/// bodies. A handler that exists but is not registered fails here rather than at F1-09 when a route
/// tries to dispatch to it.</para>
/// </summary>
public sealed class FinanceApplicationFixture : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private ServiceProvider _provider = null!;

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>F1-08. Finance's Budget-gate design is publish-and-consume, so the tests that prove it
    /// must drive the real outbox rather than calling the consumer directly — asserting on a handler
    /// invoked by hand would prove the handler works and say nothing about whether the event is ever
    /// published, routed, or delivered to it.</summary>
    public OutboxProcessor OutboxProcessor { get; private set; } = null!;

    /// <summary>Drains the outbox until it stops producing work, so a chain of events (a posting
    /// publishing a verdict, whose consumer satisfies a gate, whose cascade publishes again) settles
    /// before assertions run. Bounded rather than looping forever — a chain that will not settle is a
    /// bug this should surface as a failed assertion, not a hang.</summary>
    public async Task DrainOutboxAsync(int passes = 5)
    {
        for (int i = 0; i < passes; i++)
        {
            await OutboxProcessor.ProcessPendingAsync(CancellationToken.None);
        }
    }

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17").Build();
        await _container.StartAsync();

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _container.GetConnectionString(),
                ["JwtSettings:Key"] = "test-signing-key-at-least-32-characters-long-000000",
                ["JwtSettings:Issuer"] = "finance-application-tests",
                ["JwtSettings:Audience"] = "finance-application-tests-client",
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
            Assembly.Load("Sites.Infrastructure"),
            Assembly.Load("Finance.Infrastructure"),
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

        await MigrateAsync("Identity.Infrastructure.Persistence.IdentityModuleDbContext, Identity.Infrastructure");
        await MigrateAsync("Hr.Infrastructure.Persistence.HrDbContext, Hr.Infrastructure");
        await MigrateAsync("Approvals.Infrastructure.Persistence.ApprovalsDbContext, Approvals.Infrastructure");
        await MigrateAsync("Crm.Infrastructure.Persistence.CrmDbContext, Crm.Infrastructure");
        await MigrateAsync("Projects.Infrastructure.Persistence.ProjectsDbContext, Projects.Infrastructure");
        await MigrateAsync("Documents.Infrastructure.Persistence.DocumentsDbContext, Documents.Infrastructure");
        await MigrateAsync("Sites.Infrastructure.Persistence.SitesDbContext, Sites.Infrastructure");
        // finance is EIGHTH, inserted between sites and notify: its foreign keys point at projects, crm
        // and hr, all migrated above, and nothing points back into it.
        await MigrateAsync("Finance.Infrastructure.Persistence.FinanceDbContext, Finance.Infrastructure");
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
public sealed class FinanceApplicationCollection : ICollectionFixture<FinanceApplicationFixture>
{
    public const string Name = "FinanceApplication";
}

file sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "Finance.Application.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.NullFileProvider();
}
