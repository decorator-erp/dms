using System.Reflection;
using BuildingBlocks.Web;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Crm.Infrastructure.Tests;

/// <summary>
/// C-8 (LC-I1): migration <c>CrmClientsHardening</c> makes <c>crm.clients.AppUserId</c> unique — one portal
/// login is one client. It opens with a guard, so on a database where two clients already share a login it
/// refuses rather than half-applies. Same shape as <see cref="CrmBriefMigrationGuardTests"/>.
/// </summary>
public sealed class CrmClientMigrationGuardTests : IAsyncLifetime
{
    private const string MigrationBefore = "20261027203645_CrmMeetingsHardening";

    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17").Build();
        await _container.StartAsync();

        await MigrateModuleAsync(
            "Identity.Infrastructure", "Identity.Infrastructure.Persistence.IdentityModuleDbContext, Identity.Infrastructure");
        await MigrateModuleAsync("Hr.Infrastructure", "Hr.Infrastructure.Persistence.HrDbContext, Hr.Infrastructure");

        await using CrmDbContext db = CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(MigrationBefore);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task ClientsHardening_RefusesToRun_WhileTwoClientsShareALogin()
    {
        await PlantAsync("""
            INSERT INTO crm.clients ("Id", "AppUserId", "Address", "IsActive", "CreatedAtUtc")
            VALUES (gen_random_uuid(), 'shared-login', '1 First St', true, now()),
                   (gen_random_uuid(), 'shared-login', '2 Second St', true, now());
            """);

        await using CrmDbContext db = CreateContext();
        PostgresException ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());

        Assert.Contains("share a portal login", ex.MessageText);
        Assert.Null(await ScalarAsync("""
            SELECT indexdef FROM pg_indexes
            WHERE schemaname = 'crm' AND tablename = 'clients' AND indexdef LIKE '%UNIQUE%' AND indexdef LIKE '%AppUserId%'
            """));
    }

    [Fact]
    public async Task ClientsHardening_MakesTheLoginUnique_OnCleanData()
    {
        await using CrmDbContext db = CreateContext();
        await db.Database.MigrateAsync();

        Assert.NotNull(await ScalarAsync("""
            SELECT indexdef FROM pg_indexes
            WHERE schemaname = 'crm' AND tablename = 'clients' AND indexdef LIKE '%UNIQUE%' AND indexdef LIKE '%AppUserId%'
            """));
    }

    private async Task PlantAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"SET session_replication_role = replica; {sql} SET session_replication_role = DEFAULT;";
        await command.ExecuteNonQueryAsync();
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private async Task MigrateModuleAsync(string assemblyName, string assemblyQualifiedContextTypeName)
    {
        IModule module = ModuleDiscovery.DiscoverModules(Assembly.Load(assemblyName)).Single();

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _container.GetConnectionString(),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        module.Register(services, configuration);

        await using ServiceProvider provider = services.BuildServiceProvider();

        Type contextType = Type.GetType(assemblyQualifiedContextTypeName)
            ?? throw new InvalidOperationException($"Could not load type '{assemblyQualifiedContextTypeName}'.");

        using IServiceScope scope = provider.CreateScope();
        var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
        await context.Database.MigrateAsync();
    }

    private CrmDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<CrmDbContext>().UseNpgsql(_container.GetConnectionString()).Options);
}
