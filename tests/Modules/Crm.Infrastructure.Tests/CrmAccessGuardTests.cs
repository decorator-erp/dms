using System.Reflection;
using BuildingBlocks.Web;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Crm.Infrastructure.Services;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel;
using Testcontainers.PostgreSql;
using Xunit;

namespace Crm.Infrastructure.Tests;

/// <summary>
/// G9-08's own verify block: both actor kinds resolve, and a caller with neither identity gets a
/// 403-shaped <c>NoStanding</c>, not a crash or a silent default. Needs a real, migrated Postgres
/// (identity + hr, so crm's own cross-schema FKs can apply; crm itself, so <see cref="CrmDbContext"/>
/// has tables to query) — same Testcontainers-based structural shape as
/// <c>Projects.Infrastructure.Tests.ProjectAccessGuardTests</c>. Requires Docker; see this task's
/// standing environment note in PROGRESS.md if this suite could not run.
/// </summary>
public sealed class CrmAccessGuardTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17").Build();
        await _container.StartAsync();

        // crm.clients."AppUserId" -> identity.users; several other crm.* FKs -> hr.employees - both
        // schemas must exist before CrmDbContext's own migration (with its raw-SQL FKs) can apply.
        await MigrateModuleAsync(
            "Identity.Infrastructure", "Identity.Infrastructure.Persistence.IdentityModuleDbContext, Identity.Infrastructure");
        await MigrateModuleAsync("Hr.Infrastructure", "Hr.Infrastructure.Persistence.HrDbContext, Hr.Infrastructure");

        await using CrmDbContext db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task ResolveActorAsync_WhenHrDirectoryResolvesAnEmployee_ReturnsEmployeeActor()
    {
        var employeeId = Guid.NewGuid();
        await using CrmDbContext db = CreateContext();
        var guard = new CrmAccessGuard(db, new StubHrDirectory(("emp-user", employeeId)));

        Result<Contracts.CrmActor> result = await guard.ResolveActorAsync("emp-user", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Contracts.CrmActorKind.Employee, result.Value!.Kind);
        Assert.Equal(employeeId, result.Value.EmployeeId);
        Assert.Null(result.Value.ClientId);
        Assert.True(result.Value.HasReadAll);
    }

    [Fact]
    public async Task ResolveActorAsync_WhenNoEmployee_ButAClientRowExists_ReturnsClientActor()
    {
        string appUserId = await SeedIdentityUserAsync();
        Guid clientId = await SeedClientAsync(appUserId);

        await using CrmDbContext db = CreateContext();
        var guard = new CrmAccessGuard(db, new StubHrDirectory());

        Result<Contracts.CrmActor> result = await guard.ResolveActorAsync(appUserId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Contracts.CrmActorKind.Client, result.Value!.Kind);
        Assert.Equal(clientId, result.Value.ClientId);
        Assert.Null(result.Value.EmployeeId);
        Assert.False(result.Value.HasReadAll);
    }

    [Fact]
    public async Task ResolveActorAsync_WhenNeitherEmployeeNorClient_ReturnsNoStanding()
    {
        await using CrmDbContext db = CreateContext();
        var guard = new CrmAccessGuard(db, new StubHrDirectory());

        Result<Contracts.CrmActor> result = await guard.ResolveActorAsync("nobody", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CrmAccessErrors.NoStanding, result.Error);
    }

    [Fact]
    public async Task ResolveActorAsync_ForClientWithAPrimaryContact_IncludesTheContactId()
    {
        string appUserId = await SeedIdentityUserAsync();
        Guid clientId = await SeedClientAsync(appUserId);
        Guid contactId = await SeedContactAsync(clientId);

        await using CrmDbContext db = CreateContext();
        var guard = new CrmAccessGuard(db, new StubHrDirectory());

        Result<Contracts.CrmActor> result = await guard.ResolveActorAsync(appUserId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(contactId, result.Value!.ContactId);
    }

    [Fact]
    public async Task ResolveActorsAsync_BatchResolvesEmployeeAndClientActorsCorrectly()
    {
        var employeeId = Guid.NewGuid();
        string employeeAppUserId = "batch-emp-user";
        string clientAppUserId = await SeedIdentityUserAsync();
        Guid clientId = await SeedClientAsync(clientAppUserId);

        await using CrmDbContext db = CreateContext();
        var guard = new CrmAccessGuard(db, new StubHrDirectory((employeeAppUserId, employeeId)));

        IReadOnlyDictionary<string, Contracts.CrmActor> result = await guard.ResolveActorsAsync(
            [employeeAppUserId, clientAppUserId, "nobody-at-all"], CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(Contracts.CrmActorKind.Employee, result[employeeAppUserId].Kind);
        Assert.Equal(employeeId, result[employeeAppUserId].EmployeeId);
        Assert.Equal(Contracts.CrmActorKind.Client, result[clientAppUserId].Kind);
        Assert.Equal(clientId, result[clientAppUserId].ClientId);
        Assert.False(result.ContainsKey("nobody-at-all"));
    }

    [Fact]
    public async Task ResolveActorsAsync_EmptyList_ReturnsEmpty()
    {
        await using CrmDbContext db = CreateContext();
        var guard = new CrmAccessGuard(db, new StubHrDirectory());

        IReadOnlyDictionary<string, Contracts.CrmActor> result = await guard.ResolveActorsAsync([], CancellationToken.None);

        Assert.Empty(result);
    }

    private async Task<string> SeedIdentityUserAsync()
    {
        string appUserId = $"user-{Guid.NewGuid():N}";

        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO identity.users
                ("Id", "FirstName", "LastName", "IsActive", "UserType", "CreatedAt",
                 "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES (@id, 'Test', 'Client', true, 0, now(), true, false, false, false, 0);
            """;
        command.Parameters.AddWithValue("id", appUserId);
        await command.ExecuteNonQueryAsync();

        return appUserId;
    }

    private async Task<Guid> SeedClientAsync(string appUserId)
    {
        await using CrmDbContext db = CreateContext();
        Client client = Client.Create(appUserId, "123 Main St").Value!;
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client.Id;
    }

    private async Task<Guid> SeedContactAsync(Guid clientId)
    {
        await using CrmDbContext db = CreateContext();
        CrmContact contact = CrmContact.Create(clientId, "Jane", "Doe", (ContactRole)1, isPrimary: true).Value!;
        db.CrmContacts.Add(contact);
        await db.SaveChangesAsync();
        return contact.Id;
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

    private CrmDbContext CreateContext()
    {
        DbContextOptionsBuilder<CrmDbContext> builder = new DbContextOptionsBuilder<CrmDbContext>().UseNpgsql(_container.GetConnectionString());
        return new CrmDbContext(builder.Options);
    }

    private sealed class StubHrDirectory : IHrDirectory
    {
        private readonly Dictionary<string, Guid> _employeesByAppUserId;

        public StubHrDirectory(params (string AppUserId, Guid EmployeeId)[] employees) =>
            _employeesByAppUserId = employees.ToDictionary(e => e.AppUserId, e => e.EmployeeId);

        public Task<Guid?> ResolveEmployeeIdAsync(string appUserId, CancellationToken ct) =>
            Task.FromResult(_employeesByAppUserId.TryGetValue(appUserId, out Guid id) ? id : (Guid?)null);

        public Task<EmployeeSummary?> GetAsync(Guid id, CancellationToken ct) =>
            throw new NotImplementedException("Not used by CrmAccessGuard.");

        public Task<IReadOnlyList<EmployeeSummary>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            throw new NotImplementedException("Not used by CrmAccessGuard.");

        public Task<IReadOnlyList<Guid>> DepartmentMembersAsync(Guid departmentId, CancellationToken ct) =>
            throw new NotImplementedException("Not used by CrmAccessGuard.");

        public Task<IReadOnlyList<DepartmentHeadSummary>> GetDepartmentHeadsAsync(IReadOnlyCollection<Guid> departmentIds, CancellationToken ct) =>
            throw new NotImplementedException("Not used by CrmAccessGuard.");

        public Task<IReadOnlyList<EmployeeProfile>> GetProfilesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            throw new NotImplementedException("Not used by CrmAccessGuard.");
    }
}
