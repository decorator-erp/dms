using BuildingBlocks.Events;
using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Approvals.Infrastructure.Tests;

/// <summary>
/// Phase 5 (5.3): the module's Infrastructure on a real Postgres. <c>ApprovalsDbContext</c> migrates on a
/// fresh database (after <c>identity</c>, whose users its foreign key points at), and every integration-event
/// handler the module registers can be built from the container. The second is what an event delivery
/// does first; a handler with an unregistered dependency would only fail when its event arrived.
/// </summary>
public sealed class ApprovalsInfrastructureTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private ServiceProvider _provider = null!;
    private List<ServiceDescriptor> _approvalsDescriptors = null!;

    [Fact]
    public async Task ApprovalsDbContext_migrates_on_a_fresh_database()
    {
        await MigrateAsync("Identity.Infrastructure.Persistence.IdentityModuleDbContext, Identity.Infrastructure");
        DbContext approvals = await MigrateAsync("Approvals.Infrastructure.Persistence.ApprovalsDbContext, Approvals.Infrastructure");

        Assert.Empty(await approvals.Database.GetPendingMigrationsAsync());

        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'approvals'", connection);
        Assert.True((long)(await command.ExecuteScalarAsync())! > 0, "The migration created no approvals tables.");
    }

    [Fact]
    public void Every_integration_event_handler_the_module_registers_resolves()
    {
        // Only what ApprovalsModule.Register added: the inbox decorator turns each handler into a factory
        // registration, so the implementation type isn't on the descriptor to filter by.
        List<Type> handlerServices = [.. _approvalsDescriptors
            .Where(d => d.ServiceType.IsGenericType
                && d.ServiceType.GetGenericTypeDefinition() == typeof(IIntegrationEventHandler<>))
            .Select(d => d.ServiceType)
            .Distinct()];

        // LIVENESS: Approvals consumes events (from Documents, Projects and others); a scan that found
        // none would pass without checking anything.
        Assert.NotEmpty(handlerServices);

        using IServiceScope scope = _provider.CreateScope();
        foreach (Type service in handlerServices)
        {
            Assert.NotEmpty(scope.ServiceProvider.GetServices(service));
        }
    }

    private async Task<DbContext> MigrateAsync(string assemblyQualifiedContextTypeName)
    {
        Type contextType = Type.GetType(assemblyQualifiedContextTypeName)
            ?? throw new InvalidOperationException($"Could not load '{assemblyQualifiedContextTypeName}'.");
        var context = (DbContext)_provider.CreateScope().ServiceProvider.GetRequiredService(contextType);
        await context.Database.MigrateAsync();
        return context;
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
                ["JwtSettings:Issuer"] = "approvals-infrastructure-tests",
                ["JwtSettings:Audience"] = "approvals-infrastructure-tests-client",
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

        // Notifications too: Approvals' handlers take INotificationService (N1-06).
        IReadOnlyList<IModule> modules = ModuleDiscovery.DiscoverModules(
            System.Reflection.Assembly.Load("Identity.Infrastructure"),
            System.Reflection.Assembly.Load("Approvals.Infrastructure"),
            System.Reflection.Assembly.Load("Notifications.Infrastructure"));
        foreach (IModule module in modules.Where(m => m.Name != "Approvals"))
        {
            module.Register(services, configuration);
        }

        int before = services.Count;
        modules.Single(m => m.Name == "Approvals").Register(services, configuration);
        _approvalsDescriptors = [.. services.Skip(before)];

        _provider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }
}

file sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "Approvals.Infrastructure.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.NullFileProvider();
}
