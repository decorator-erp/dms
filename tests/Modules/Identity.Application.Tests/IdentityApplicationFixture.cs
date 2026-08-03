using BuildingBlocks;
using BuildingBlocks.Outbox;
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

namespace Identity.Application.Tests;

/// <summary>
/// Boots the same DI graph <see cref="Identity.Infrastructure.IdentityModule.Register"/> builds against
/// a real Postgres (Testcontainers) — including <c>AddModuleCqrs&lt;IdentityModuleDbContext&gt;</c> over
/// the real <c>Identity.Application</c> assembly, so handlers resolve wrapped in their actual
/// Validation → Logging → DomainEventDispatch chain, not a hand-wired substitute. Also migrates
/// <see cref="SharedDbContext"/> first, since <c>RegisterCommandHandler</c> stages a row in
/// <c>shared.outbox_messages</c> in the same <c>SaveChangesAsync</c> as the new user.
/// </summary>
public sealed class IdentityApplicationFixture : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder().WithImage("postgres:17").Build();
        await _container.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Key"] = "test-signing-key-at-least-32-characters-long-000000",
                ["JwtSettings:Issuer"] = "identity-application-tests",
                ["JwtSettings:Audience"] = "identity-application-tests-client",
                ["JwtSettings:ExpiryMinutes"] = "60",
                ["JwtSettings:RefreshTokenExpiryDays"] = "14",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<IdentityModuleDbContext>(o => o.UseNpgsql(_container.GetConnectionString()));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<IdentityModuleDbContext>()
            .AddDefaultTokenProviders();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations();

        services.AddScoped<IJwtProvider, JwtProvider>();
        services.AddScoped<IEffectivePermissionService, EffectivePermissionService>();

        services.AddModuleCqrs<IdentityModuleDbContext>(
            typeof(Identity.Application.Features.Auths.Register.RegisterCommand).Assembly);

        _provider = services.BuildServiceProvider();

        await using (var shared = new SharedDbContext(
            new DbContextOptionsBuilder<SharedDbContext>().UseNpgsql(_container.GetConnectionString()).Options))
        {
            await shared.Database.MigrateAsync();
        }

        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>().Database.MigrateAsync();
    }

    public AsyncServiceScope CreateScope() => _provider.CreateAsyncScope();

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class IdentityApplicationCollection : ICollectionFixture<IdentityApplicationFixture>
{
    public const string Name = "IdentityApplication";
}
