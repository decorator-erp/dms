using BuildingBlocks;
using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Xunit;

namespace Documents.Application.Tests;

/// <summary>
/// Boots all five modules' real DI graphs (Identity, HR, Approvals, Projects, Documents) plus
/// <c>SharedDbContext</c>/<c>AddOutbox()</c> against one Testcontainers Postgres — same shape as
/// <c>ProjectsApplicationFixture</c>, one module wider. Needed because Documents' own folder-scoped
/// commands (<c>CreateStageFolderCommand</c>/<c>CreateTaskFolderCommand</c>) resolve a real stage/task
/// via <c>Projects.Contracts.IProjectDirectory</c> — no manual stage/task creation command exists
/// anywhere in Projects.Contracts, only template instantiation, so getting a real stage/task id for a
/// Documents test means going through the full Identity → HR → Projects flow, not a shortcut.
/// </summary>
public sealed class DocumentsApplicationFixture : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private ServiceProvider _provider = null!;

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
                ["JwtSettings:Issuer"] = "documents-application-tests",
                ["JwtSettings:Audience"] = "documents-application-tests-client",
                ["JwtSettings:ExpiryMinutes"] = "60",
                ["JwtSettings:RefreshTokenExpiryDays"] = "14",
                ["FileStorage:RootPath"] = Path.Combine(Path.GetTempPath(), "dms-modular-tests", Guid.NewGuid().ToString("N")),
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
            System.Reflection.Assembly.Load("Hr.Infrastructure"),
            System.Reflection.Assembly.Load("Approvals.Infrastructure"),
            System.Reflection.Assembly.Load("Projects.Infrastructure"),
            System.Reflection.Assembly.Load("Documents.Infrastructure"));

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
        await MigrateAsync("Projects.Infrastructure.Persistence.ProjectsDbContext, Projects.Infrastructure");
        await MigrateAsync("Documents.Infrastructure.Persistence.DocumentsDbContext, Documents.Infrastructure");

        var identityModule = modules.Single(m => m.Name == "Identity");
        await using (var scope = _provider.CreateAsyncScope())
        {
            await identityModule.SeedAsync(scope.ServiceProvider);
        }
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
public sealed class DocumentsApplicationCollection : ICollectionFixture<DocumentsApplicationFixture>
{
    public const string Name = "DocumentsApplication";
}

file sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "Documents.Application.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.NullFileProvider();
}
