using System.Reflection;
using BuildingBlocks.Web;
using Hr.Domain;
using Hr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Hr.Infrastructure.Tests;

/// <summary>
/// G2-03's verify block: migration applies, the cross-schema FK exists in <c>pg_constraint</c>,
/// <c>AppUserId</c> has a unique index, and EF metadata for <see cref="Employee"/> contains no
/// navigation to <c>ApplicationUser</c> and no relationship targeting an <c>identity.</c> table.
/// Migrates <c>identity</c> before <c>hr</c> (same order <see cref="ModuleDependencyOrder"/> requires
/// in the real host) via each module's own public <c>IModule.Register</c>, exactly as
/// <c>Integration.Tests.ApiFixture</c> and <c>Rule9CrossSchemaForeignKeyTests</c> do.
/// </summary>
public sealed class HrSchemaTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17").Build();
        await _container.StartAsync();

        await MigrateModuleAsync(
            "Identity.Infrastructure", "Identity.Infrastructure.Persistence.IdentityModuleDbContext, Identity.Infrastructure");
        await MigrateModuleAsync(
            "Hr.Infrastructure", "Hr.Infrastructure.Persistence.HrDbContext, Hr.Infrastructure");
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task Migration_creates_the_cross_schema_foreign_key_with_a_unique_index()
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();

        await using NpgsqlCommand fkCommand = connection.CreateCommand();
        fkCommand.CommandText = """
            SELECT con.conname, con.confdeltype
            FROM pg_constraint con
            JOIN pg_class src ON src.oid = con.conrelid
            JOIN pg_namespace src_ns ON src_ns.oid = src.relnamespace
            WHERE con.contype = 'f' AND src_ns.nspname = 'hr' AND src.relname = 'employees';
            """;

        await using NpgsqlDataReader fkReader = await fkCommand.ExecuteReaderAsync();
        Assert.True(await fkReader.ReadAsync(), "Expected a foreign key on hr.employees.");
        Assert.Equal("fk_employees_app_user", fkReader.GetString(0));
        Assert.Equal('r', fkReader.GetChar(1)); // 'r' = RESTRICT, never CASCADE across a module boundary.
        await fkReader.CloseAsync();

        await using NpgsqlCommand indexCommand = connection.CreateCommand();
        indexCommand.CommandText = """
            SELECT ix.indisunique
            FROM pg_index ix
            JOIN pg_class idx ON idx.oid = ix.indexrelid
            JOIN pg_class tbl ON tbl.oid = ix.indrelid
            JOIN pg_namespace ns ON ns.oid = tbl.relnamespace
            WHERE ns.nspname = 'hr' AND tbl.relname = 'employees' AND idx.relname = 'IX_employees_AppUserId';
            """;

        await using NpgsqlDataReader indexReader = await indexCommand.ExecuteReaderAsync();
        Assert.True(await indexReader.ReadAsync(), "Expected a unique index on hr.employees.AppUserId.");
        Assert.True(indexReader.GetBoolean(0));
    }

    /// <summary>
    /// HR Phase 2 (K1, K9, N8, Aug 2.7): the constraints the migration adds exist, with the semantics the
    /// rules need. Every in-module foreign key is RESTRICT (<c>confdeltype = 'r'</c>), never CASCADE; the
    /// two unique indexes are partial (<c>WHERE "IsActive"</c>, so a removed membership or a deleted
    /// department doesn't count); the contract-type CHECK is there.
    /// </summary>
    [Fact]
    public async Task Phase2_constraints_are_restrict_foreign_keys_partial_unique_indexes_and_a_check()
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();

        Dictionary<string, char> foreignKeys = [];
        await using (NpgsqlCommand fk = connection.CreateCommand())
        {
            fk.CommandText = """
                SELECT con.conname, con.confdeltype
                FROM pg_constraint con
                JOIN pg_class src ON src.oid = con.conrelid
                JOIN pg_namespace ns ON ns.oid = src.relnamespace
                WHERE con.contype = 'f' AND ns.nspname = 'hr' AND src.relname IN ('employee_departments', 'departments');
                """;
            await using NpgsqlDataReader reader = await fk.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                foreignKeys[reader.GetString(0)] = reader.GetChar(1);
            }
        }

        Assert.Equal('r', foreignKeys["FK_employee_departments_employees_EmployeeId"]);
        Assert.Equal('r', foreignKeys["FK_employee_departments_departments_DepartmentId"]);
        Assert.Equal('r', foreignKeys["FK_departments_employees_DepartmentHeadId"]);

        foreach (string index in new[] { "IX_employee_departments_EmployeeId", "IX_departments_Name" })
        {
            await using NpgsqlCommand ix = connection.CreateCommand();
            ix.CommandText = """
                SELECT ix.indisunique, pg_get_expr(ix.indpred, ix.indrelid)
                FROM pg_index ix
                JOIN pg_class idx ON idx.oid = ix.indexrelid
                JOIN pg_namespace ns ON ns.oid = idx.relnamespace
                WHERE ns.nspname = 'hr' AND idx.relname = @name;
                """;
            ix.Parameters.AddWithValue("name", index);
            await using NpgsqlDataReader reader = await ix.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), $"Expected index {index}.");
            Assert.True(reader.GetBoolean(0), $"{index} is not unique.");
            Assert.Contains("IsActive", reader.GetString(1));
        }

        await using NpgsqlCommand check = connection.CreateCommand();
        check.CommandText = """
            SELECT pg_get_constraintdef(con.oid)
            FROM pg_constraint con
            JOIN pg_namespace ns ON ns.oid = con.connamespace
            WHERE ns.nspname = 'hr' AND con.contype = 'c' AND con.conname = 'ck_employees_contract_type';
            """;
        string? definition = (string?)await check.ExecuteScalarAsync();
        // Postgres stores BETWEEN as the pair of comparisons it means.
        Assert.NotNull(definition);
        Assert.Contains("\"ContractType\" >= 1", definition);
        Assert.Contains("\"ContractType\" <= 4", definition);
    }

    [Fact]
    public void EmployeeEntityType_HasNoNavigationOrRelationshipTargetingIdentity()
    {
        using var context = new HrDbContext(
            new DbContextOptionsBuilder<HrDbContext>().UseNpgsql(_container.GetConnectionString()).Options);

        IEntityType employeeType = context.Model.FindEntityType(typeof(Employee))!;

        // PC-03 liveness: all three assertions below read EF metadata, and all three are satisfied by a
        // metadata reader that returns nothing. The property list proves the model is really built and
        // really describes Employee — including the scalar AppUserId, which is precisely the column the
        // absent navigation would otherwise have been mapped through.
        Assert.NotEmpty(employeeType.GetProperties());
        Assert.Contains(employeeType.GetProperties(), p => p.Name == nameof(Employee.AppUserId));

        Assert.Empty(employeeType.GetNavigations());
        Assert.Empty(employeeType.GetForeignKeys());
        Assert.DoesNotContain(
            employeeType.GetProperties(),
            p => p.ClrType.Namespace?.StartsWith("Identity.", StringComparison.Ordinal) == true);
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
}
