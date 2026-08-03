using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// Boots the real Api host (<c>WebApplicationFactory&lt;Program&gt;</c>) against a Testcontainers
/// Postgres, migrating every module schema in <see cref="ModuleDependencyOrder.SchemaOrder"/> — today
/// that's just <c>shared</c>; add each module's <c>*.Infrastructure</c> DbContext here (and its
/// ProjectReference to this test project) as Identity (G1-03) and HR (G2-03) are built.
///
/// The JWT signing key is a fixed test value injected via configuration, never the real dev
/// user-secrets value — tests must not depend on (or leak into) a developer's local secrets.
/// </summary>
public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string JwtKey = "integration-test-signing-key-at-least-32-bytes-long-000000";
    public const string JwtIssuer = "DMS.Modular.Api.Tests";
    public const string JwtAudience = "DMS.Modular.Client.Tests";

    private Testcontainers.PostgreSql.PostgreSqlContainer _container = null!;
    private Respawner _respawner = null!;

    public string ConnectionString => _container.GetConnectionString();

    public string IssueTestToken(string userId, params string[] permissions) =>
        TestJwt.Issue(JwtKey, JwtIssuer, JwtAudience, userId, permissions);

    public async Task InitializeAsync()
    {
        _container = new Testcontainers.PostgreSql.PostgreSqlBuilder().WithImage("postgres:17").Build();
        await _container.StartAsync();

        foreach (var schema in ModuleDependencyOrder.SchemaOrder)
            await MigrateSchemaAsync(schema);

        // The string-connection overload of CreateAsync only supports SQL Server (it constructs a
        // SqlConnection internally) - Postgres needs the DbConnection overload with an explicit
        // NpgsqlConnection.
        await using var respawnConnection = new NpgsqlConnection(ConnectionString);
        await respawnConnection.OpenAsync();

        _respawner = await Respawner.CreateAsync(respawnConnection, new RespawnerOptions
        {
            // __EFMigrationsHistory lives in the default "public" schema regardless of a context's
            // HasDefaultSchema (confirmed by inspection), so restricting to these three schemas already
            // leaves it untouched — no explicit TablesToIgnore needed.
            SchemasToInclude = ["identity", "hr", "shared"],
            DbAdapter = DbAdapter.Postgres,
        });

        // Force the host to build now (rather than lazily on first request) so a configuration or
        // startup failure surfaces here, in InitializeAsync, not confusingly inside the first test.
        _ = Services;
    }

    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _container.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting, not ConfigureAppConfiguration + AddInMemoryCollection: settings applied this way
        // are guaranteed highest precedence, added directly to the host's own configuration rather than
        // as one more provider whose ordering relative to Program.cs's own setup (appsettings.json,
        // user secrets, env vars) would otherwise have to be reasoned about.
        builder.UseEnvironment("Development");
        builder.UseSetting("JwtSettings:Key", JwtKey);
        builder.UseSetting("JwtSettings:Issuer", JwtIssuer);
        builder.UseSetting("JwtSettings:Audience", JwtAudience);
        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
    }

    private async Task MigrateSchemaAsync(string schema)
    {
        switch (schema)
        {
            case "shared":
                await using (var db = new SharedDbContext(
                    new DbContextOptionsBuilder<SharedDbContext>().UseNpgsql(ConnectionString).Options))
                {
                    await db.Database.MigrateAsync();
                }
                break;

            case "identity":
                await MigrateViaHostAsync("Identity.Infrastructure.Persistence.IdentityModuleDbContext, Identity.Infrastructure");
                break;

                // "hr" has no migration to apply yet — HR lands at G2-03.
        }
    }

    /// <summary>
    /// Resolves a module's DbContext by assembly-qualified name and migrates it, without the test
    /// project ever naming the internal concrete type — every module DbContext is internal (arch rule
    /// 4), so there is no compile-time way to reference it from outside the module, and there
    /// shouldn't be. Accessing <see cref="WebApplicationFactory{TEntryPoint}.Services"/> here forces
    /// the host to build (using the connection string set in <see cref="ConfigureWebHost"/>), which is
    /// what makes this "migrate through the real composition root", not a bespoke test-only context.
    /// </summary>
    private async Task MigrateViaHostAsync(string assemblyQualifiedContextTypeName)
    {
        var contextType = Type.GetType(assemblyQualifiedContextTypeName)
            ?? throw new InvalidOperationException($"Could not load type '{assemblyQualifiedContextTypeName}'.");

        using var scope = Services.CreateScope();
        var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
        await context.Database.MigrateAsync();
    }
}
