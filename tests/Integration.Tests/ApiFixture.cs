using System.Collections.Concurrent;
using System.Reflection;
using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    /// <summary>
    /// Every SQL statement EF Core has executed across every DbContext in this host, captured via a
    /// custom <see cref="ILoggerProvider"/> registered in <see cref="ConfigureWebHost"/> — for G3-01's
    /// "enable EF query logging in the test and assert no SQL touches the identity schema" requirement.
    /// Call <see cref="ClearCapturedSql"/> before the operation under test so earlier noise (migrations,
    /// seeding, an unrelated request) doesn't produce a false positive.
    /// </summary>
    public IReadOnlyList<string> CapturedSql => [.. _capturedSql];

    public void ClearCapturedSql() => _capturedSql.Clear();

    private readonly ConcurrentQueue<string> _capturedSql = new();

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
            SchemasToInclude = ["identity", "hr", "approvals", "shared"],
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
        builder.ConfigureLogging(logging => logging.AddProvider(new SqlCapturingLoggerProvider(_capturedSql)));
    }

    /// <summary>Captures every message logged under EF Core's "Executed DbCommand" event — the SQL text
    /// itself, schema-qualified table names included — across every DbContext in the host.</summary>
    private sealed class SqlCapturingLoggerProvider(ConcurrentQueue<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new SqlCapturingLogger(categoryName, sink);

        public void Dispose()
        {
        }

        private sealed class SqlCapturingLogger(string categoryName, ConcurrentQueue<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) =>
                categoryName == "Microsoft.EntityFrameworkCore.Database.Command" && logLevel >= LogLevel.Information;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel) && eventId.Id == Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted.Id)
                    sink.Enqueue(formatter(state, exception));
            }
        }
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
                await MigrateModuleSchemaAsync(
                    "Identity.Infrastructure",
                    "Identity.Infrastructure.Persistence.IdentityModuleDbContext, Identity.Infrastructure");
                break;

            case "hr":
                await MigrateModuleSchemaAsync(
                    "Hr.Infrastructure",
                    "Hr.Infrastructure.Persistence.HrDbContext, Hr.Infrastructure");
                break;

            case "approvals":
                await MigrateModuleSchemaAsync(
                    "Approvals.Infrastructure",
                    "Approvals.Infrastructure.Persistence.ApprovalsDbContext, Approvals.Infrastructure");
                break;
        }
    }

    /// <summary>
    /// Resolves a module's DbContext by assembly-qualified name and migrates it, without the test
    /// project ever naming the internal concrete type — every module DbContext is internal (arch rule
    /// 4), so there is no compile-time way to reference it from outside the module, and there
    /// shouldn't be.
    ///
    /// Deliberately does NOT go through <see cref="WebApplicationFactory{TEntryPoint}.Services"/> (the
    /// real host's container) — since G1-07, <c>Program.cs</c> runs every <c>IModule.SeedAsync</c> as
    /// part of building that host, and seeding queries tables that don't exist yet on a fresh
    /// Testcontainers database. Building a throwaway <see cref="ServiceCollection"/> via the module's
    /// own (public) <see cref="IModule.Register"/> — the same discovery mechanism
    /// <c>Program.cs</c> uses, just not <c>Program.cs</c>'s own container — resolves and migrates the
    /// DbContext with no seeding side effect, so migration always completes before the real host (and
    /// its seed step) ever builds.
    /// </summary>
    private async Task MigrateModuleSchemaAsync(string moduleAssemblyName, string assemblyQualifiedContextTypeName)
    {
        var module = ModuleDiscovery.DiscoverModules(Assembly.Load(moduleAssemblyName)).Single();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        module.Register(services, configuration);

        await using var provider = services.BuildServiceProvider();

        var contextType = Type.GetType(assemblyQualifiedContextTypeName)
            ?? throw new InvalidOperationException($"Could not load type '{assemblyQualifiedContextTypeName}'.");

        using var scope = provider.CreateScope();
        var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
        await context.Database.MigrateAsync();
    }
}
