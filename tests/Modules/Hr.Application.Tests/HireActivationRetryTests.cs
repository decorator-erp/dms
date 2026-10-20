using BuildingBlocks.Events;
using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using Hr.Contracts;
using Hr.Contracts.Employees;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel;
using Testcontainers.PostgreSql;
using Xunit;

namespace Hr.Application.Tests;

/// <summary>
/// <b>A co-handler failure cannot permanently block a new employee from logging in.</b>
///
/// <para><c>EmployeeHiredIntegrationEvent</c> has two subscribers — HR's activation handler and
/// Notifications' welcome handler — and <c>EventBus</c> runs them sequentially, rethrowing the first
/// failure, so a handler that throws prevents the ones behind it from running on that delivery. This
/// asserts the recovery: <c>OutboxProcessor</c> leaves the message unprocessed on failure, and the next
/// sweep gets the activation through.</para>
///
/// <para><b>What this does NOT prove.</b> It is not a test of inbox granularity. Under a hypothetical
/// per-event inbox the first delivery would roll back without writing any row, the second would rerun
/// every handler, and this test would pass identically. The property coarsening would actually break is
/// a co-handler's side effect running twice — which for the real co-handler is prevented a layer lower,
/// by <c>INotificationService</c>'s mandatory dedupe key, so no test is asserted here for it.</para>
///
/// <para>Its own container and provider, because the failing handler must be registered BEFORE
/// <c>HrModule.Register</c>'s scan for <c>EventBus</c> to invoke it first — registered last it would
/// never block activation and the test would prove nothing. Note it is not wrapped in
/// <c>InboxDedupeDecorator</c> (that only decorates the module scan's own descriptors), so the
/// throw-once flag stands in for the missing inbox row.</para>
/// </summary>
public sealed class HireActivationRetryTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private ServiceProvider _provider = null!;
    private OutboxProcessor _outboxProcessor = null!;

    [Fact]
    public async Task A_co_handler_that_fails_once_delays_activation_but_does_not_prevent_it()
    {
        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
        IServiceProvider sp = scope.ServiceProvider;

        Result<Guid> hired = await sp.GetRequiredService<ICommandHandler<CreateEmployeeCommand, Guid>>()
            .HandleAsync(new CreateEmployeeCommand(
                $"retry-{Guid.NewGuid():N}@example.com", "P@ssw0rd123!", "Retry", "Case",
                "Engineer", DateOnly.FromDateTime(DateTime.UtcNow), "FullTime"));
        Assert.True(hired.IsSuccess, hired.IsFailure ? hired.Error.ToString() : null);

        string appUserId = (await sp.GetRequiredService<IHrDirectory>()
            .GetAsync(hired.Value, CancellationToken.None))!.AppUserId;

        IUserDirectory userDirectory = sp.GetRequiredService<IUserDirectory>();

        // First delivery: the co-handler throws before the activation handler is reached.
        await _outboxProcessor.ProcessPendingAsync(CancellationToken.None);

        Assert.True(ThrowOnceHandler.WasCalled, "The failing co-handler never ran, so this proves nothing.");
        Assert.False(
            (await userDirectory.GetAsync(appUserId, CancellationToken.None))!.IsActive,
            "Activation ran even though a handler ahead of it threw - EventBus is expected to stop at the "
            + "first failure, so either that changed or the registration order did.");

        // Second delivery: the message was never marked processed, so the sweep retries it.
        await _outboxProcessor.ProcessPendingAsync(CancellationToken.None);

        Assert.True(
            (await userDirectory.GetAsync(appUserId, CancellationToken.None))!.IsActive,
            "The hire event was retried but the employee's login was still never activated.");
    }

    /// <summary>Throws on its first invocation only, then succeeds — standing in for a transient
    /// failure in a handler that shares this event.</summary>
    private sealed class ThrowOnceHandler : IIntegrationEventHandler<EmployeeHiredIntegrationEvent>
    {
        private static int _calls;

        public static bool WasCalled => Volatile.Read(ref _calls) > 0;

        public static void Reset() => Volatile.Write(ref _calls, 0);

        public Task HandleAsync(EmployeeHiredIntegrationEvent @event, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                throw new InvalidOperationException("Deliberate first-delivery failure.");
            }

            return Task.CompletedTask;
        }
    }

    public async Task InitializeAsync()
    {
        ThrowOnceHandler.Reset();

        _container = new PostgreSqlBuilder("postgres:17").Build();
        await _container.StartAsync();

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _container.GetConnectionString(),
                ["JwtSettings:Key"] = "test-signing-key-at-least-32-characters-long-000000",
                ["JwtSettings:Issuer"] = "hr-hire-retry-tests",
                ["JwtSettings:Audience"] = "hr-hire-retry-tests-client",
                ["JwtSettings:ExpiryMinutes"] = "60",
                ["JwtSettings:RefreshTokenExpiryDays"] = "14",

                // No backoff: this is about the retry happening at all, not about when. The backoff
                // itself is covered by BuildingBlocks' OutboxDeadLetterTests.
                ["Outbox:BackoffBaseSeconds"] = "0",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new RetryTestHostEnvironment());

        services.AddDbContext<SharedDbContext>(o => o.UseNpgsql(_container.GetConnectionString()));
        services.AddOutbox();

        // BEFORE the module scan. EventBus invokes handlers in DI-registration order, so this is what
        // puts the failing handler ahead of HR's activation handler.
        services.AddScoped<IIntegrationEventHandler<EmployeeHiredIntegrationEvent>, ThrowOnceHandler>();

        IReadOnlyList<IModule> modules = ModuleDiscovery.DiscoverModules(
            System.Reflection.Assembly.Load("Identity.Infrastructure"),
            System.Reflection.Assembly.Load("Hr.Infrastructure"));

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

        IModule identityModule = modules.Single(m => m.Name == "Identity");
        await using (AsyncServiceScope scope = _provider.CreateAsyncScope())
        {
            await identityModule.SeedAsync(scope.ServiceProvider);
        }

        _outboxProcessor = _provider.GetServices<IHostedService>().OfType<OutboxProcessor>().Single();
    }

    private async Task MigrateAsync(string assemblyQualifiedContextTypeName)
    {
        Type contextType = Type.GetType(assemblyQualifiedContextTypeName)
            ?? throw new InvalidOperationException($"Could not load '{assemblyQualifiedContextTypeName}'.");

        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
        var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }
}

/// <summary>"Development", so <c>AdminUserSeeder</c> skips rather than throwing on the unset
/// <c>DMS_SEED_ADMIN_PASSWORD</c> — same reasoning as <c>HrApplicationFixture</c>'s own copy, which is
/// <c>file</c>-scoped and therefore not shared.</summary>
file sealed class RetryTestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "Hr.Application.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.NullFileProvider();
}
