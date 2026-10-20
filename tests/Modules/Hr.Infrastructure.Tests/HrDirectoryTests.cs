using System.Reflection;
using BuildingBlocks.Web;
using Hr.Contracts;
using Hr.Domain;
using Hr.Infrastructure.Persistence;
using Hr.Infrastructure.Services;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Hr.Infrastructure.Tests;

/// <summary>
/// G2-06's own verify block, checked structurally against a real database rather than by inspection
/// alone: <c>GetManyAsync</c> with 100 ids issues exactly one SQL command and zero calls to
/// <see cref="IUserDirectory"/>; <c>GetProfilesAsync</c> with 100 ids issues exactly one HR command and
/// exactly one <c>IUserDirectory</c> call; <c>ResolveEmployeeIdAsync</c> called three times with the
/// same id hits the database once. Counts SQL commands via <c>DbContextOptionsBuilder.LogTo</c> filtered
/// to <see cref="RelationalEventId.CommandExecuted"/> — the same "enable EF query logging in the test"
/// technique the plan's own G3-01 task calls for elsewhere.
/// </summary>
public sealed class HrDirectoryTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17").Build();
        await _container.StartAsync();

        // hr.employees."AppUserId" has a real FK to identity.users("Id") - identity must migrate first.
        await MigrateModuleAsync(
            "Identity.Infrastructure", "Identity.Infrastructure.Persistence.IdentityModuleDbContext, Identity.Infrastructure");

        await using HrDbContext db = CreateContext(_ => { });
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

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

    [Fact]
    public async Task GetManyAsync_with_many_ids_issues_one_query_and_never_calls_IUserDirectory()
    {
        Guid[] ids = await SeedEmployeesAsync(100);

        int commandCount = 0;
        await using HrDbContext db = CreateContext(_ => Interlocked.Increment(ref commandCount));
        var directory = new HrDirectory(db, new ThrowingUserDirectory());

        IReadOnlyList<EmployeeSummary> results = await directory.GetManyAsync(ids, CancellationToken.None);

        Assert.Equal(100, results.Count);
        Assert.Equal(1, commandCount);
    }

    [Fact]
    public async Task GetProfilesAsync_with_many_ids_issues_one_hr_query_and_one_identity_call()
    {
        Guid[] ids = await SeedEmployeesAsync(100);

        int commandCount = 0;
        await using HrDbContext db = CreateContext(_ => Interlocked.Increment(ref commandCount));
        var userDirectory = new CountingUserDirectory();
        var directory = new HrDirectory(db, userDirectory);

        IReadOnlyList<EmployeeProfile> profiles = await directory.GetProfilesAsync(ids, CancellationToken.None);

        Assert.Equal(100, profiles.Count);
        Assert.Equal(1, commandCount);
        Assert.Equal(1, userDirectory.GetManyAsyncCallCount);
    }

    [Fact]
    public async Task ResolveEmployeeIdAsync_called_three_times_with_the_same_id_hits_the_database_once()
    {
        Guid[] ids = await SeedEmployeesAsync(1);

        await using HrDbContext seedScope = CreateContext(_ => { });
        Employee employee = await seedScope.Employees.AsNoTracking().FirstAsync(e => e.Id == ids[0]);

        int commandCount = 0;
        await using HrDbContext db = CreateContext(_ => Interlocked.Increment(ref commandCount));
        var directory = new HrDirectory(db, new ThrowingUserDirectory());

        Guid? first = await directory.ResolveEmployeeIdAsync(employee.AppUserId, CancellationToken.None);
        Guid? second = await directory.ResolveEmployeeIdAsync(employee.AppUserId, CancellationToken.None);
        Guid? third = await directory.ResolveEmployeeIdAsync(employee.AppUserId, CancellationToken.None);

        Assert.Equal(employee.Id, first);
        Assert.Equal(employee.Id, second);
        Assert.Equal(employee.Id, third);
        Assert.Equal(1, commandCount);
    }

    /// <summary>
    /// Each employee needs a real <c>identity.users</c> row - <c>hr.employees."AppUserId"</c> carries a
    /// genuine <c>ON DELETE RESTRICT</c> foreign key (G2-03), so an employee referencing a nonexistent
    /// user id fails to insert at all, not just at some later join. Inserted via raw SQL rather than
    /// through <c>UserManager</c> - this test only needs a row to satisfy the constraint, not full
    /// ASP.NET Identity semantics.
    /// </summary>
    /// <summary>
    /// K3: GetAsync, GetManyAsync, ResolveEmployeeIdAsync and DepartmentMembersAsync see active employees
    /// only, each still in one statement (the filter is part of the same SQL). IsLinkedAsync is the one
    /// "deactivated too" question (A1): a deactivated employee's login is still linked.
    /// </summary>
    [Fact]
    public async Task A_deactivated_employee_is_invisible_to_the_directory_but_still_linked()
    {
        Guid[] ids = await SeedEmployeesAsync(2);
        Guid active = ids[0];
        Guid deactivated = ids[1];
        Guid departmentId;
        string deactivatedAppUserId;

        await using (HrDbContext seed = CreateContext(_ => { }))
        {
            var department = Department.Create($"Dept {Guid.NewGuid():N}");
            seed.Departments.Add(department);
            seed.EmployeeDepartments.Add(EmployeeDepartment.Create(active, department.Id));
            seed.EmployeeDepartments.Add(EmployeeDepartment.Create(deactivated, department.Id));
            await seed.SaveChangesAsync();
            departmentId = department.Id;

            Employee toDeactivate = await seed.Employees.FirstAsync(e => e.Id == deactivated);
            deactivatedAppUserId = toDeactivate.AppUserId;
            toDeactivate.Deactivate();
            await seed.SaveChangesAsync();
        }

        int commandCount = 0;
        await using HrDbContext db = CreateContext(_ => Interlocked.Increment(ref commandCount));
        var directory = new HrDirectory(db, new ThrowingUserDirectory());

        EmployeeSummary? single = await directory.GetAsync(deactivated, CancellationToken.None);
        IReadOnlyList<EmployeeSummary> many = await directory.GetManyAsync(ids, CancellationToken.None);
        IReadOnlyList<Guid> members = await directory.DepartmentMembersAsync(departmentId, CancellationToken.None);
        Guid? resolved = await directory.ResolveEmployeeIdAsync(deactivatedAppUserId, CancellationToken.None);

        Assert.Null(single);
        Assert.Equal([active], many.Select(e => e.Id));
        Assert.Equal([active], members);
        Assert.Null(resolved);
        Assert.Equal(4, commandCount);

        Assert.True(await directory.IsLinkedAsync(deactivatedAppUserId, CancellationToken.None));
        Assert.Equal([deactivatedAppUserId], await directory.GetLinkedAsync([deactivatedAppUserId, "nobody"], CancellationToken.None));
    }

    private async Task<Guid[]> SeedEmployeesAsync(int count)
    {
        string[] appUserIds = Enumerable.Range(0, count).Select(i => $"user-{Guid.NewGuid():N}-{i}").ToArray();

        await using (var connection = new Npgsql.NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();
            foreach (string? appUserId in appUserIds)
            {
                await using NpgsqlCommand command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO identity.users
                        ("Id", "FirstName", "LastName", "IsActive", "UserType", "CreatedAt",
                         "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
                    VALUES (@id, 'Test', 'User', true, 0, now(), true, false, false, false, 0);
                    """;
                command.Parameters.AddWithValue("id", appUserId);
                await command.ExecuteNonQueryAsync();
            }
        }

        await using HrDbContext db = CreateContext(_ => { });
        var employees = appUserIds
            .Select(appUserId => Employee.Create(
                appUserId, "Engineer", DateOnly.FromDateTime(DateTime.UtcNow), ContractType.FullTime))
            .ToList();

        db.Employees.AddRange(employees);
        await db.SaveChangesAsync();

        return [.. employees.Select(e => e.Id)];
    }

    private HrDbContext CreateContext(Action<string> onCommandExecuted)
    {
        DbContextOptionsBuilder<HrDbContext> builder = new DbContextOptionsBuilder<HrDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .LogTo(
                message => onCommandExecuted(message),
                [Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted],
                LogLevel.Information);

        return new HrDbContext(builder.Options);
    }

    private sealed class ThrowingUserDirectory : IUserDirectory
    {
        public Task<UserSummary?> GetAsync(string userId, CancellationToken ct) =>
            throw new InvalidOperationException("GetManyAsync must not call IUserDirectory at all.");

        public Task<IReadOnlyList<UserSummary>> GetManyAsync(IReadOnlyCollection<string> ids, CancellationToken ct) =>
            throw new InvalidOperationException("GetManyAsync must not call IUserDirectory at all.");
    }

    private sealed class CountingUserDirectory : IUserDirectory
    {
        public int GetManyAsyncCallCount { get; private set; }

        public Task<UserSummary?> GetAsync(string userId, CancellationToken ct) =>
            throw new InvalidOperationException("Not used by GetProfilesAsync.");

        public Task<IReadOnlyList<UserSummary>> GetManyAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
        {
            GetManyAsyncCallCount++;
            IReadOnlyList<UserSummary> summaries = [.. ids.Select(id => new UserSummary(id, $"{id}@example.com", "Test User", true))];
            return Task.FromResult(summaries);
        }
    }
}
