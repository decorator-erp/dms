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
/// The CRM brief fixes' M1/M2 migration (<c>CrmBriefStageRequired</c>) opens with two data guards, so it
/// refuses — rather than half-applies — on rows the new constraints cannot hold: a stage-less brief, and an
/// entry superseded twice. Each test migrates CRM to the migration before it, plants the offending rows
/// (foreign keys suspended for the plant only, since the rows are the point, not their parents), and
/// asserts the migration fails with the guard's message and leaves the schema as it was.
/// </summary>
public sealed class CrmBriefMigrationGuardTests : IAsyncLifetime
{
    private const string MigrationBefore = "20261018072206_CrmBriefApprovalRounds";

    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17").Build();
        await _container.StartAsync();

        // Same order as CrmAccessGuardTests: crm's cross-schema FKs need identity and hr first.
        await MigrateModuleAsync(
            "Identity.Infrastructure", "Identity.Infrastructure.Persistence.IdentityModuleDbContext, Identity.Infrastructure");
        await MigrateModuleAsync("Hr.Infrastructure", "Hr.Infrastructure.Persistence.HrDbContext, Hr.Infrastructure");

        await using CrmDbContext db = CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(MigrationBefore);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task StageRequired_RefusesToRun_WhileABriefHasNoStage()
    {
        await PlantAsync("""
            INSERT INTO crm.crm_design_briefs
                ("Id", "ProjectId", "ProjectStageId", "CreatedById", "Status", "UnitType", "UnitCondition",
                 "HiringReasons", "IsActive", "CreatedAtUtc")
            VALUES (gen_random_uuid(), gen_random_uuid(), NULL, gen_random_uuid(), 1, 1, 1, 0, true, now());
            """);

        await using CrmDbContext db = CreateContext();
        PostgresException ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());

        Assert.Contains("NULL ProjectStageId", ex.MessageText);
        Assert.Equal("YES", await ScalarAsync("""
            SELECT is_nullable FROM information_schema.columns
            WHERE table_schema = 'crm' AND table_name = 'crm_design_briefs' AND column_name = 'ProjectStageId'
            """));
    }

    [Fact]
    public async Task StageRequired_RefusesToRun_WhileAnEntryIsSupersededTwice()
    {
        await PlantAsync("""
            INSERT INTO crm.crm_design_briefs
                ("Id", "ProjectId", "ProjectStageId", "CreatedById", "Status", "UnitType", "UnitCondition",
                 "HiringReasons", "IsActive", "CreatedAtUtc")
            VALUES ('00000000-0000-0000-0000-0000000000b1', gen_random_uuid(), gen_random_uuid(), gen_random_uuid(),
                    1, 1, 1, 0, true, now());
            INSERT INTO crm.crm_brief_spaces ("Id", "BriefId", "ApprovalStatus", "CreatedById", "IsActive", "CreatedAtUtc", "SupersedesSpaceId")
            VALUES ('00000000-0000-0000-0000-0000000000e1', '00000000-0000-0000-0000-0000000000b1', 3, gen_random_uuid(), true, now(), NULL),
                   (gen_random_uuid(), '00000000-0000-0000-0000-0000000000b1', 1, gen_random_uuid(), true, now(), '00000000-0000-0000-0000-0000000000e1'),
                   (gen_random_uuid(), '00000000-0000-0000-0000-0000000000b1', 1, gen_random_uuid(), true, now(), '00000000-0000-0000-0000-0000000000e1');
            """);

        await using CrmDbContext db = CreateContext();
        PostgresException ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());

        Assert.Contains("superseded more than once", ex.MessageText);
        Assert.Null(await ScalarAsync("""
            SELECT indexdef FROM pg_indexes
            WHERE schemaname = 'crm' AND indexname = 'IX_crm_brief_spaces_SupersedesSpaceId' AND indexdef LIKE '%UNIQUE%'
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
