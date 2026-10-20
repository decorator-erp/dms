using System.Reflection;
using BuildingBlocks.Web;
using Hr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Hr.Infrastructure.Tests;

/// <summary>
/// HR Phase 2 (K9): each guard at the top of <c>HrDataIntegrity</c> refuses to migrate over data that
/// breaks the constraint it protects, and a refused migration changes nothing. Every test starts from a
/// database migrated to the migration before it and plants the bad rows directly, foreign keys off
/// (<c>session_replication_role = replica</c>), the same way <c>CrmBriefMigrationGuardTests</c> does.
/// </summary>
public sealed class HrDataIntegrityMigrationGuardTests : IAsyncLifetime
{
    private const string MigrationBefore = "20260827091145_UpdateEmployeeEntity";

    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17").Build();
        await _container.StartAsync();

        await MigrateModuleAsync(
            "Identity.Infrastructure", "Identity.Infrastructure.Persistence.IdentityModuleDbContext, Identity.Infrastructure");

        await using HrDbContext db = CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(MigrationBefore);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task Refuses_an_employee_with_two_active_memberships()
    {
        await PlantAsync("""
            INSERT INTO hr.employee_departments ("EmployeeId", "DepartmentId", "IsActive", "CreatedAtUtc")
            VALUES ('00000000-0000-0000-0000-0000000000e1', gen_random_uuid(), true, now()),
                   ('00000000-0000-0000-0000-0000000000e1', gen_random_uuid(), true, now());
            """);

        await AssertRefusedAsync("more than one active membership");
    }

    [Fact]
    public async Task Refuses_a_contract_type_outside_one_to_four()
    {
        await PlantAsync("""
            INSERT INTO hr.employees ("Id", "JobTitle", "HireDate", "ContractType", "AppUserId", "IsActive", "CreatedAtUtc")
            VALUES (gen_random_uuid(), 'Engineer', CURRENT_DATE, 99, 'no-such-user', true, now());
            """);

        await AssertRefusedAsync("ContractType outside 1..4");
    }

    [Fact]
    public async Task Refuses_a_department_head_that_is_not_an_employee()
    {
        await PlantAsync("""
            INSERT INTO hr.departments ("Id", "Name", "DepartmentHeadId", "IsActive", "CreatedAtUtc")
            VALUES (gen_random_uuid(), 'Ghost Head', gen_random_uuid(), true, now());
            """);

        await AssertRefusedAsync("DepartmentHeadId that is not an employee");
    }

    /// <summary>The old plain unique index makes duplicate names impossible on a database that has it,
    /// so this guard only matters for one that drifted. The test simulates that by dropping the index.</summary>
    [Fact]
    public async Task Refuses_two_active_departments_with_the_same_name()
    {
        await PlantAsync("""
            DROP INDEX hr."IX_departments_Name";
            INSERT INTO hr.departments ("Id", "Name", "IsActive", "CreatedAtUtc")
            VALUES (gen_random_uuid(), 'Sales', true, now()), (gen_random_uuid(), 'Sales', true, now());
            """);

        await AssertRefusedAsync("two active departments with the same name");
    }

    /// <summary>Positive control: clean data migrates, and the new start date is backfilled from the
    /// creation date rather than left at the scaffold's year 1.</summary>
    [Fact]
    public async Task Clean_data_migrates_and_StartedAtUtc_is_backfilled_from_CreatedAtUtc()
    {
        // Real parent rows: the migration re-adds the join-table foreign keys, which checks every row.
        await PlantAsync("""
            INSERT INTO hr.employees ("Id", "JobTitle", "HireDate", "ContractType", "AppUserId", "IsActive", "CreatedAtUtc")
            VALUES ('00000000-0000-0000-0000-0000000000e2', 'Engineer', CURRENT_DATE, 1, 'clean-user', true, now());
            INSERT INTO hr.departments ("Id", "Name", "IsActive", "CreatedAtUtc")
            VALUES ('00000000-0000-0000-0000-0000000000d2', 'Clean', true, now());
            INSERT INTO hr.employee_departments ("EmployeeId", "DepartmentId", "IsActive", "CreatedAtUtc")
            VALUES ('00000000-0000-0000-0000-0000000000e2', '00000000-0000-0000-0000-0000000000d2', true, '2025-03-04T05:06:07Z');
            """);

        await using (HrDbContext db = CreateContext())
        {
            await db.Database.MigrateAsync();
        }

        Assert.Equal(true, await ScalarAsync("""
            SELECT bool_and("StartedAtUtc" = "CreatedAtUtc") FROM hr.employee_departments
            """));
    }

    private async Task AssertRefusedAsync(string expectedMessage)
    {
        await using HrDbContext db = CreateContext();
        PostgresException ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());

        Assert.Contains(expectedMessage, ex.MessageText);

        // Nothing was changed: the guards run before any DDL, and the migration is not recorded.
        Assert.Equal(0L, await ScalarAsync("""
            SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'hr' AND c.relname = 'IX_employee_departments_EmployeeId'
            """));
        Assert.Equal(0L, await ScalarAsync("""
            SELECT count(*) FROM information_schema.columns
            WHERE table_schema = 'hr' AND table_name = 'employee_departments' AND column_name = 'StartedAtUtc'
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

    private HrDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<HrDbContext>().UseNpgsql(_container.GetConnectionString()).Options);
}
