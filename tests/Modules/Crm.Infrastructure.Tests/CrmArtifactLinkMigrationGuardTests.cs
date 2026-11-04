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
/// L-4 (CRM production review): migration <c>CrmArtifactLinkUnique</c> makes a document's link to a subject
/// unique, one partial index per subject column. It opens with a guard per column, so on a database that
/// already holds a duplicate link it refuses before any DDL rather than half-applies. Same shape as
/// <see cref="CrmClientMigrationGuardTests"/>.
/// </summary>
public sealed class CrmArtifactLinkMigrationGuardTests : IAsyncLifetime
{
    private const string MigrationBefore = "20261028022621_CrmClientsHardening";

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
    public async Task ArtifactLinkUnique_RefusesToRun_WhileADocumentIsLinkedToOneMeetingTwice()
    {
        await PlantAsync("""
            INSERT INTO crm.crm_artifact_documents ("Id", "DocumentId", "MeetingId", "AddedByEmployeeId", "IsActive", "CreatedAtUtc")
            VALUES (gen_random_uuid(), '00000000-0000-0000-0000-0000000000d1', '00000000-0000-0000-0000-0000000000a1', gen_random_uuid(), true, now()),
                   (gen_random_uuid(), '00000000-0000-0000-0000-0000000000d1', '00000000-0000-0000-0000-0000000000a1', gen_random_uuid(), true, now());
            """);

        await using CrmDbContext db = CreateContext();
        PostgresException ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());

        Assert.Contains("same MeetingId more than once", ex.MessageText);
        Assert.Equal(0L, await ScalarAsync("""
            SELECT count(*) FROM pg_indexes
            WHERE schemaname = 'crm' AND indexname LIKE 'ux_crm_artifact_documents_document_%'
            """));
    }

    [Fact]
    public async Task ArtifactLinkUnique_BuildsOnePartialUniqueIndexPerSubject_OnCleanData()
    {
        await using CrmDbContext db = CreateContext();
        await db.Database.MigrateAsync();

        Assert.Equal(4L, await ScalarAsync("""
            SELECT count(*) FROM pg_indexes
            WHERE schemaname = 'crm' AND tablename = 'crm_artifact_documents'
              AND indexname LIKE 'ux_crm_artifact_documents_document_%'
              AND indexdef LIKE '%UNIQUE%' AND indexdef LIKE '%WHERE%'
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
