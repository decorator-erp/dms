using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using BuildingBlocks.Pagination;
using BuildingBlocks.Web;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Notifications.Contracts;
using SharedKernel;
using Testcontainers.PostgreSql;
using Xunit;

namespace Notifications.Application.Tests;

/// <summary>
/// Phase 5 (5.3): Notifications' Application handlers on a real Postgres. A notification written
/// through <see cref="INotificationService"/> comes back through the inbox query for its recipient, newest
/// first, and never in another user's inbox.
/// </summary>
public sealed class NotificationsApplicationTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private ServiceProvider _provider = null!;

    [Fact]
    public async Task The_inbox_query_returns_the_recipients_notifications_newest_first_and_nobody_elses()
    {
        string recipient = await ProvisionUserAsync();
        string other = await ProvisionUserAsync();

        await using (AsyncServiceScope scope = _provider.CreateAsyncScope())
        {
            INotificationService notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
            await notifications.NotifyAsync(recipient, NotificationType.TaskAssigned, "first");
            await notifications.NotifyAsync(other, NotificationType.TaskAssigned, "someone else's");
            await notifications.NotifyAsync(recipient, NotificationType.TaskAssigned, "second");
        }

        await using AsyncServiceScope query = _provider.CreateAsyncScope();
        Result<PagedResult<NotificationResponse>> result = await query.ServiceProvider
            .GetRequiredService<IQueryHandler<GetMyNotificationsQuery, PagedResult<NotificationResponse>>>()
            .HandleAsync(new GetMyNotificationsQuery(recipient, UnreadOnly: false, PageNumber: 1, PageSize: 20));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        Assert.Equal(2, result.Value!.TotalCount);
        Assert.Equal(["second", "first"], result.Value.Items.Select(n => n.Title));
        Assert.All(result.Value.Items, n => Assert.False(n.IsRead));
    }

    private async Task<string> ProvisionUserAsync()
    {
        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
        string email = $"notify-{Guid.NewGuid():N}@example.com";
        Result<string> created = await scope.ServiceProvider.GetRequiredService<IUserProvisioning>().CreateAsync(
            new NewUserRequest(email, "P@ssw0rd123!", "Notify", "Test", null, UserKind.Employee, IdentityRoles.Employee),
            CancellationToken.None);
        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);
        return created.Value!;
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
                ["JwtSettings:Issuer"] = "notifications-application-tests",
                ["JwtSettings:Audience"] = "notifications-application-tests-client",
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

        IReadOnlyList<IModule> modules = ModuleDiscovery.DiscoverModules(
            System.Reflection.Assembly.Load("Identity.Infrastructure"),
            System.Reflection.Assembly.Load("Notifications.Infrastructure"));
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

        // identity before notify: notify's foreign keys point at identity.users.
        await MigrateAsync("Identity.Infrastructure.Persistence.IdentityModuleDbContext, Identity.Infrastructure");
        await MigrateAsync("Notifications.Infrastructure.Persistence.NotificationsDbContext, Notifications.Infrastructure");

        await using AsyncServiceScope seed = _provider.CreateAsyncScope();
        await modules.Single(m => m.Name == "Identity").SeedAsync(seed.ServiceProvider);
    }

    private async Task MigrateAsync(string assemblyQualifiedContextTypeName)
    {
        Type contextType = Type.GetType(assemblyQualifiedContextTypeName)
            ?? throw new InvalidOperationException($"Could not load '{assemblyQualifiedContextTypeName}'.");
        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
        await ((DbContext)scope.ServiceProvider.GetRequiredService(contextType)).Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }
}

/// <summary>"Development", so <c>AdminUserSeeder</c> skips rather than throwing on the unset
/// <c>DMS_SEED_ADMIN_PASSWORD</c>.</summary>
file sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "Notifications.Application.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.NullFileProvider();
}
