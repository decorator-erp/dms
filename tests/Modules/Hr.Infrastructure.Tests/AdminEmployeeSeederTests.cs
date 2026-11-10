using System.Reflection;
using BuildingBlocks.Web;
using Hr.Domain;
using Hr.Infrastructure.Persistence;
using Hr.Infrastructure.Persistence.Seeders;
using Identity.Contracts;
using Identity.Contracts.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Hr.Infrastructure.Tests;

/// <summary>
/// Every login is an employee or a client: the seeded admin (<see cref="SeedAccounts.AdminUserId"/>)
/// gets its employee profile from <see cref="AdminEmployeeSeeder"/>, once, and a database seeded before
/// this rule (an admin user with no employee row) is repaired on the next start.
/// </summary>
public sealed class AdminEmployeeSeederTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17").Build();
        await _container.StartAsync();

        // hr.employees."AppUserId" has a real FK to identity.users("Id") - identity must migrate first.
        IModule identity = ModuleDiscovery.DiscoverModules(Assembly.Load("Identity.Infrastructure")).Single();
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _container.GetConnectionString(),
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        identity.Register(services, configuration);
        await using (ServiceProvider provider = services.BuildServiceProvider())
        {
            Type contextType = Type.GetType(
                "Identity.Infrastructure.Persistence.IdentityModuleDbContext, Identity.Infrastructure")!;
            using IServiceScope scope = provider.CreateScope();
            await ((DbContext)scope.ServiceProvider.GetRequiredService(contextType)).Database.MigrateAsync();
        }

        await using HrDbContext db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task Seeding_twice_gives_the_admin_exactly_one_employee_profile()
    {
        await InsertAdminUserAsync();
        var users = new AdminOnlyUserDirectory();

        await using (HrDbContext db = CreateContext())
        {
            await AdminEmployeeSeeder.SeedAsync(db, users);
        }

        await using (HrDbContext db = CreateContext())
        {
            await AdminEmployeeSeeder.SeedAsync(db, users);
        }

        await using HrDbContext read = CreateContext();
        List<Employee> rows = await read.Employees.IgnoreQueryFilters()
            .Where(e => e.AppUserId == SeedAccounts.AdminUserId).ToListAsync();
        Employee admin = Assert.Single(rows);
        Assert.Equal((AdminEmployeeSeeder.JobTitle, ContractType.FullTime, true), (admin.JobTitle, admin.ContractType, admin.IsActive));
    }

    [Fact]
    public async Task An_existing_profile_is_left_alone_even_when_inactive()
    {
        await InsertAdminUserAsync();
        await using (HrDbContext db = CreateContext())
        {
            var existing = Employee.Create(
                SeedAccounts.AdminUserId, "Owner", new DateOnly(2026, 1, 1), ContractType.Contract);
            existing.Deactivate();
            db.Employees.Add(existing);
            await db.SaveChangesAsync();
        }

        await using (HrDbContext db = CreateContext())
        {
            await AdminEmployeeSeeder.SeedAsync(db, new AdminOnlyUserDirectory());
        }

        await using HrDbContext read = CreateContext();
        Employee only = Assert.Single(await read.Employees.IgnoreQueryFilters()
            .Where(e => e.AppUserId == SeedAccounts.AdminUserId).ToListAsync());
        Assert.Equal("Owner", only.JobTitle);
    }

    [Fact]
    public async Task No_seeded_admin_means_nothing_is_added()
    {
        await using (HrDbContext db = CreateContext())
        {
            await AdminEmployeeSeeder.SeedAsync(db, new EmptyUserDirectory());
        }

        await using HrDbContext read = CreateContext();
        Assert.False(await read.Employees.IgnoreQueryFilters().AnyAsync(e => e.AppUserId == SeedAccounts.AdminUserId));
    }

    private async Task InsertAdminUserAsync()
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO identity.users
                ("Id", "FirstName", "LastName", "IsActive", "UserType", "CreatedAt",
                 "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES (@id, 'System', 'Administrator', true, 0, now(), true, false, false, false, 0)
            ON CONFLICT ("Id") DO NOTHING;
            """;
        command.Parameters.AddWithValue("id", SeedAccounts.AdminUserId);
        await command.ExecuteNonQueryAsync();

        // Each test starts from no admin employee row.
        await using NpgsqlCommand clear = connection.CreateCommand();
        clear.CommandText = """DELETE FROM hr.employees WHERE "AppUserId" = @id;""";
        clear.Parameters.AddWithValue("id", SeedAccounts.AdminUserId);
        await clear.ExecuteNonQueryAsync();
    }

    private HrDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<HrDbContext>().UseNpgsql(_container.GetConnectionString()).Options);

    private sealed class AdminOnlyUserDirectory : IUserDirectory
    {
        public Task<UserSummary?> GetAsync(string userId, CancellationToken ct) =>
            Task.FromResult<UserSummary?>(userId == SeedAccounts.AdminUserId
                ? new UserSummary(userId, "admin@system.dms", "System Administrator", true)
                : null);

        public Task<IReadOnlyList<UserSummary>> GetManyAsync(IReadOnlyCollection<string> ids, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class EmptyUserDirectory : IUserDirectory
    {
        public Task<UserSummary?> GetAsync(string userId, CancellationToken ct) => Task.FromResult<UserSummary?>(null);

        public Task<IReadOnlyList<UserSummary>> GetManyAsync(IReadOnlyCollection<string> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<UserSummary>>([]);
    }
}
