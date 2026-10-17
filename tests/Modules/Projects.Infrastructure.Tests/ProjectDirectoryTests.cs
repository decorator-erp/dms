using System.Reflection;
using BuildingBlocks.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Projects.Contracts;
using Projects.Infrastructure.Persistence;
using Projects.Infrastructure.Services;
using Testcontainers.PostgreSql;
using Xunit;
using Domain = Projects.Domain;

namespace Projects.Infrastructure.Tests;

/// <summary>
/// F1-00b's verify block. <c>ProjectSummary.OwnerId</c> exists so Finance's <c>InvoiceDueJob</c> can
/// resolve a project owner as a notification recipient without reading <c>projects.projects</c>.
///
/// <para>The assertion that matters is that the projection carries the <b>owner that project was
/// actually created with</b>, not that the field is populated at all. Two distinct employees are
/// seeded and the project is given the second one, so a projection that accidentally mapped
/// <c>ClientId</c>, an arbitrary employee, or a default <c>Guid</c> fails — whereas
/// <c>Assert.NotEqual(Guid.Empty, summary.OwnerId)</c> would pass against all three. The value is a
/// direct return of the call under test, so no liveness control applies here (the PC-04 criterion):
/// nothing is gathered by machinery.</para>
/// </summary>
public sealed class ProjectDirectoryTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17").Build();
        await _container.StartAsync();

        // Same dependency chain ProjectAccessGuardTests documents: the raw-SQL FKs on projects need
        // identity, hr, approvals and crm to exist before ProjectsDbContext's own migration applies.
        await MigrateModuleAsync(
            "Identity.Infrastructure", "Identity.Infrastructure.Persistence.IdentityModuleDbContext, Identity.Infrastructure");
        await MigrateModuleAsync("Hr.Infrastructure", "Hr.Infrastructure.Persistence.HrDbContext, Hr.Infrastructure");
        await MigrateModuleAsync(
            "Approvals.Infrastructure", "Approvals.Infrastructure.Persistence.ApprovalsDbContext, Approvals.Infrastructure");
        await MigrateModuleAsync("Crm.Infrastructure", "Crm.Infrastructure.Persistence.CrmDbContext, Crm.Infrastructure");

        await using ProjectsDbContext db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task GetProjectAsync_ProjectsTheOwnerTheProjectWasCreatedWith()
    {
        // A decoy owner, seeded first, so "returns some employee id" cannot pass.
        Guid decoyOwnerId = await SeedEmployeeAsync();
        Guid realOwnerId = await SeedEmployeeAsync();
        Assert.NotEqual(decoyOwnerId, realOwnerId);

        (Guid projectId, Guid clientId) = await SeedProjectAsync(realOwnerId);

        await using ProjectsDbContext db = CreateContext();
        var directory = new ProjectDirectory(db);

        ProjectSummary? summary = await directory.GetProjectAsync(projectId, CancellationToken.None);

        Assert.NotNull(summary);
        Assert.Equal(realOwnerId, summary!.OwnerId);
        Assert.NotEqual(decoyOwnerId, summary.OwnerId);
        // ClientId is a different Guid column on the same row - asserted separately so a projection
        // that transposed the two trailing arguments fails here rather than reading as correct.
        Assert.Equal(clientId, summary.ClientId);
    }

    [Fact]
    public async Task GetProjectsAsync_BatchSibling_CarriesTheOwnerToo()
    {
        Guid ownerA = await SeedEmployeeAsync();
        Guid ownerB = await SeedEmployeeAsync();
        (Guid projectA, Guid _) = await SeedProjectAsync(ownerA);
        (Guid projectB, Guid _) = await SeedProjectAsync(ownerB);

        await using ProjectsDbContext db = CreateContext();
        var directory = new ProjectDirectory(db);

        IReadOnlyList<ProjectSummary> summaries = await directory.GetProjectsAsync([projectA, projectB], CancellationToken.None);

        Assert.Equal(2, summaries.Count);
        Assert.Equal(ownerA, summaries.Single(s => s.Id == projectA).OwnerId);
        Assert.Equal(ownerB, summaries.Single(s => s.Id == projectB).OwnerId);
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

    /// <summary>Raw SQL for the reason <see cref="ProjectAccessGuardTests"/> documents: this test
    /// project holds no <c>InternalsVisibleTo</c> grant from <c>Hr.Domain</c> or <c>Crm.Domain</c>.</summary>
    private async Task<Guid> SeedEmployeeAsync()
    {
        string appUserId = $"user-{Guid.NewGuid():N}";
        var employeeId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await InsertUserAsync(connection, appUserId, "Test", "Owner");

        await using NpgsqlCommand employeeCommand = connection.CreateCommand();
        employeeCommand.CommandText = """
            INSERT INTO hr.employees
                ("Id", "JobTitle", "HireDate", "ContractType", "AppUserId", "IsActive", "CreatedAtUtc")
            VALUES (@id, 'Engineer', CURRENT_DATE, 1, @appUserId, true, now());
            """;
        employeeCommand.Parameters.AddWithValue("id", employeeId);
        employeeCommand.Parameters.AddWithValue("appUserId", appUserId);
        await employeeCommand.ExecuteNonQueryAsync();

        return employeeId;
    }

    private async Task<(Guid ProjectId, Guid ClientId)> SeedProjectAsync(Guid ownerId)
    {
        Guid clientId = await SeedClientAsync();
        var project = Domain.Project.Create(
            $"P-{Guid.NewGuid():N}", "Test Project", "123 Main St", Domain.WorkflowType.Stage, clientId, ownerId);

        await using ProjectsDbContext db = CreateContext();
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        return (project.Id, clientId);
    }

    private async Task<Guid> SeedClientAsync()
    {
        string clientAppUserId = $"client-{Guid.NewGuid():N}";
        var clientId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await InsertUserAsync(connection, clientAppUserId, "Test", "Client");

        await using NpgsqlCommand clientCommand = connection.CreateCommand();
        clientCommand.CommandText = """
            INSERT INTO crm.clients ("Id", "AppUserId", "Address", "IsActive", "CreatedAtUtc")
            VALUES (@id, @appUserId, '1 Client Rd', true, now());
            """;
        clientCommand.Parameters.AddWithValue("id", clientId);
        clientCommand.Parameters.AddWithValue("appUserId", clientAppUserId);
        await clientCommand.ExecuteNonQueryAsync();

        return clientId;
    }

    private static async Task InsertUserAsync(NpgsqlConnection connection, string id, string firstName, string lastName)
    {
        await using NpgsqlCommand userCommand = connection.CreateCommand();
        userCommand.CommandText = """
            INSERT INTO identity.users
                ("Id", "FirstName", "LastName", "IsActive", "UserType", "CreatedAt",
                 "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES (@id, @firstName, @lastName, true, 0, now(), true, false, false, false, 0);
            """;
        userCommand.Parameters.AddWithValue("id", id);
        userCommand.Parameters.AddWithValue("firstName", firstName);
        userCommand.Parameters.AddWithValue("lastName", lastName);
        await userCommand.ExecuteNonQueryAsync();
    }

    private ProjectsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ProjectsDbContext>().UseNpgsql(_container.GetConnectionString()).Options);
}
