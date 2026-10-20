using Identity.Contracts;
using Identity.Domain;
using Identity.Infrastructure.Authentication;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Identity.Infrastructure.Tests;

/// <summary>
/// Real Postgres (Testcontainers), a real <c>IdentityModuleDbContext</c> and real
/// <c>UserManager</c>/<c>RoleManager</c> — this is security-sensitive code (permission resolution,
/// token issuance), and InMemory EF's looser query translation is exactly the kind of gap that could
/// hide a real bug here. Has <c>InternalsVisibleTo</c> access to both Identity.Infrastructure and
/// Identity.Domain (naming: <c>Identity.Infrastructure.Tests</c>, distinct from the pure-domain
/// <c>Identity.UnitTests</c> — this one needs a database).
/// </summary>
public sealed class IdentityInfrastructureFixture : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17").Build();
        await _container.StartAsync();

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Key"] = "test-signing-key-at-least-32-characters-long-000000",
                ["JwtSettings:Issuer"] = "identity-infrastructure-tests",
                ["JwtSettings:Audience"] = "identity-infrastructure-tests-client",
                ["JwtSettings:ExpiryMinutes"] = "60",
                ["JwtSettings:RefreshTokenExpiryDays"] = "14",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<IdentityModuleDbContext>(o => o.UseNpgsql(_container.GetConnectionString()));
        services.AddIdentityCore<ApplicationUser>(IdentityPolicy.Configure)
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<IdentityModuleDbContext>()
            .AddDefaultTokenProviders();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations();

        services.AddScoped<IJwtProvider, JwtProvider>();
        services.AddScoped<IEffectivePermissionService, EffectivePermissionService>();
        services.AddScoped<IUserProvisioning, UserProvisioning>();

        _provider = services.BuildServiceProvider();

        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>().Database.MigrateAsync();
    }

    public AsyncServiceScope CreateScope() => _provider.CreateAsyncScope();

    /// <summary>For tests that build their own context, e.g. to count the commands a call issues.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class IdentityInfrastructureCollection : ICollectionFixture<IdentityInfrastructureFixture>
{
    public const string Name = "IdentityInfrastructure";
}
