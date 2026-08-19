using System.Reflection;
using BuildingBlocks.Web;
using Hr.Domain;
using Hr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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

        await using var fkCommand = connection.CreateCommand();
        fkCommand.CommandText = """
            SELECT con.conname, con.confdeltype
            FROM pg_constraint con
            JOIN pg_class src ON src.oid = con.conrelid
            JOIN pg_namespace src_ns ON src_ns.oid = src.relnamespace
            WHERE con.contype = 'f' AND src_ns.nspname = 'hr' AND src.relname = 'employees';
            """;

        await using var fkReader = await fkCommand.ExecuteReaderAsync();
        Assert.True(await fkReader.ReadAsync(), "Expected a foreign key on hr.employees.");
        Assert.Equal("fk_employees_app_user", fkReader.GetString(0));
        Assert.Equal('r', fkReader.GetChar(1)); // 'r' = RESTRICT, never CASCADE across a module boundary.
        await fkReader.CloseAsync();

        await using var indexCommand = connection.CreateCommand();
        indexCommand.CommandText = """
            SELECT ix.indisunique
            FROM pg_index ix
            JOIN pg_class idx ON idx.oid = ix.indexrelid
            JOIN pg_class tbl ON tbl.oid = ix.indrelid
            JOIN pg_namespace ns ON ns.oid = tbl.relnamespace
            WHERE ns.nspname = 'hr' AND tbl.relname = 'employees' AND idx.relname = 'IX_employees_AppUserId';
            """;

        await using var indexReader = await indexCommand.ExecuteReaderAsync();
        Assert.True(await indexReader.ReadAsync(), "Expected a unique index on hr.employees.AppUserId.");
        Assert.True(indexReader.GetBoolean(0));
    }

    [Fact]
    public void EmployeeEntityType_HasNoNavigationOrRelationshipTargetingIdentity()
    {
        using var context = new HrDbContext(
            new DbContextOptionsBuilder<HrDbContext>().UseNpgsql(_container.GetConnectionString()).Options);

        var employeeType = context.Model.FindEntityType(typeof(Employee))!;

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
        var module = ModuleDiscovery.DiscoverModules(Assembly.Load(assemblyName)).Single();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _container.GetConnectionString(),
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
