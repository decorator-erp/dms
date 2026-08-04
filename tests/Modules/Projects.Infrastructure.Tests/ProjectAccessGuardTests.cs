using System.Reflection;
using BuildingBlocks;
using BuildingBlocks.Web;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Projects.Contracts;
using Projects.Infrastructure.Persistence;
using Projects.Infrastructure.Services;
using Testcontainers.PostgreSql;
using Xunit;
using Domain = Projects.Domain;

namespace Projects.Infrastructure.Tests;

/// <summary>
/// G5-05's own verify block, checked structurally against a real database: no permission + not a
/// member → denied; <c>ReadAll</c> → allowed regardless; member → allowed; batch filter over 50 projects
/// issues exactly one membership query. Counts SQL commands via <c>LogTo</c> filtered to
/// <see cref="Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted"/>, same
/// technique <c>HrDirectoryTests</c> (G2-06) established.
/// </summary>
public sealed class ProjectAccessGuardTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder().WithImage("postgres:17").Build();
        await _container.StartAsync();

        // projects.projects."OwnerId" -> hr.employees, hr.employees."AppUserId" -> identity.users,
        // projects.gate_conditions."ApprovalId" -> approvals.approvals - all three must exist before
        // ProjectsDbContext's own migration (with its raw-SQL FKs) can apply.
        await MigrateModuleAsync(
            "Identity.Infrastructure", "Identity.Infrastructure.Persistence.IdentityModuleDbContext, Identity.Infrastructure");
        await MigrateModuleAsync("Hr.Infrastructure", "Hr.Infrastructure.Persistence.HrDbContext, Hr.Infrastructure");
        await MigrateModuleAsync(
            "Approvals.Infrastructure", "Approvals.Infrastructure.Persistence.ApprovalsDbContext, Approvals.Infrastructure");

        await using var db = CreateContext(_ => { });
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

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

    [Fact]
    public async Task EnsureProjectAccessAsync_NoPermission_NotAMember_IsDenied()
    {
        var (appUserId, employeeId) = await SeedEmployeeAsync();
        var projectId = await SeedProjectAsync(employeeId);

        await using var db = CreateContext(_ => { });
        var guard = new ProjectAccessGuard(db, new StubHrDirectory(employeeId), new StubCurrentUser(hasReadAll: false));

        var result = await guard.EnsureProjectAccessAsync(projectId, appUserId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(Domain.ProjectErrors.AccessDenied, result.Error);
    }

    [Fact]
    public async Task EnsureProjectAccessAsync_ReadAllPermission_IsAllowedRegardlessOfMembership()
    {
        var (appUserId, employeeId) = await SeedEmployeeAsync();
        var projectId = await SeedProjectAsync(employeeId);

        await using var db = CreateContext(_ => { });
        var guard = new ProjectAccessGuard(db, new StubHrDirectory(employeeId), new StubCurrentUser(hasReadAll: true));

        var result = await guard.EnsureProjectAccessAsync(projectId, appUserId, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task EnsureProjectAccessAsync_StageTeamMember_IsAllowed()
    {
        var (appUserId, employeeId) = await SeedEmployeeAsync();
        var projectId = await SeedProjectAsync(employeeId);
        await SeedStageTeamMembershipAsync(projectId, employeeId);

        await using var db = CreateContext(_ => { });
        var guard = new ProjectAccessGuard(db, new StubHrDirectory(employeeId), new StubCurrentUser(hasReadAll: false));

        var result = await guard.EnsureProjectAccessAsync(projectId, appUserId, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task FilterAccessibleProjectsAsync_Over50Projects_IssuesExactlyOneMembershipQuery()
    {
        var (appUserId, employeeId) = await SeedEmployeeAsync();
        var accessibleIds = new List<Guid>();
        for (var i = 0; i < 50; i++)
        {
            var projectId = await SeedProjectAsync(employeeId);
            await SeedStageTeamMembershipAsync(projectId, employeeId);
            accessibleIds.Add(projectId);
        }

        var inaccessibleId = await SeedProjectAsync(employeeId);

        var commandCount = 0;
        await using var db = CreateContext(_ => Interlocked.Increment(ref commandCount));
        var guard = new ProjectAccessGuard(db, new StubHrDirectory(employeeId), new StubCurrentUser(hasReadAll: false));

        var allIds = new List<Guid>(accessibleIds) { inaccessibleId };
        var result = await guard.FilterAccessibleProjectsAsync(allIds, appUserId, CancellationToken.None);

        Assert.Equal(50, result.Count);
        Assert.DoesNotContain(inaccessibleId, result);
        Assert.Equal(1, commandCount);
    }

    [Fact]
    public async Task FilterAccessibleProjectsAsync_EmptyList_ReturnsEmpty_NoQuery()
    {
        var commandCount = 0;
        await using var db = CreateContext(_ => Interlocked.Increment(ref commandCount));
        var guard = new ProjectAccessGuard(db, new StubHrDirectory(Guid.NewGuid()), new StubCurrentUser(hasReadAll: false));

        var result = await guard.FilterAccessibleProjectsAsync([], "some-user", CancellationToken.None);

        Assert.Empty(result);
        Assert.Equal(0, commandCount);
    }

    /// <summary>
    /// Raw SQL, not <c>Hr.Domain.Employee.Create</c> - <c>Hr.Domain</c> grants no
    /// <c>InternalsVisibleTo</c> to this test project (correctly; it isn't part of HR), same reasoning
    /// <c>HrApplicationFixture</c> documents for why cross-module test fixtures resolve real modules by
    /// name rather than constructing their internal types directly.
    /// </summary>
    private async Task<(string AppUserId, Guid EmployeeId)> SeedEmployeeAsync()
    {
        var appUserId = $"user-{Guid.NewGuid():N}";
        var employeeId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();

        await using (var userCommand = connection.CreateCommand())
        {
            userCommand.CommandText = """
                INSERT INTO identity.users
                    ("Id", "FirstName", "LastName", "IsActive", "UserType", "CreatedAt",
                     "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
                VALUES (@id, 'Test', 'User', true, 0, now(), true, false, false, false, 0);
                """;
            userCommand.Parameters.AddWithValue("id", appUserId);
            await userCommand.ExecuteNonQueryAsync();
        }

        await using (var employeeCommand = connection.CreateCommand())
        {
            employeeCommand.CommandText = """
                INSERT INTO hr.employees
                    ("Id", "JobTitle", "HireDate", "ContractType", "AppUserId", "IsActive", "CreatedAtUtc")
                VALUES (@id, 'Engineer', CURRENT_DATE, 1, @appUserId, true, now());
                """;
            employeeCommand.Parameters.AddWithValue("id", employeeId);
            employeeCommand.Parameters.AddWithValue("appUserId", appUserId);
            await employeeCommand.ExecuteNonQueryAsync();
        }

        return (appUserId, employeeId);
    }

    private async Task<Guid> SeedProjectAsync(Guid ownerId)
    {
        var project = Domain.Project.Create(
            $"P-{Guid.NewGuid():N}", "Test Project", "123 Main St", Domain.WorkflowType.Stage, Guid.NewGuid(), ownerId);

        await using var db = CreateContext(_ => { });
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        return project.Id;
    }

    private async Task SeedStageTeamMembershipAsync(Guid projectId, Guid employeeId)
    {
        await using var db = CreateContext(_ => { });

        var stage = Domain.ProjectStage.Create("Design", 1, projectId);
        db.ProjectStages.Add(stage);
        await db.SaveChangesAsync();

        var team = Domain.StageTeam.CreateProjectStandaloneTeam(stage.Id, employeeId);
        var addResult = team.AddMember(employeeId, Domain.StageTeamMemberRole.Responsible);
        Assert.True(addResult.IsSuccess);
        db.StageTeams.Add(team);
        await db.SaveChangesAsync();
    }

    private ProjectsDbContext CreateContext(Action<string> onCommandExecuted)
    {
        var builder = new DbContextOptionsBuilder<ProjectsDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .LogTo(
                message => onCommandExecuted(message),
                [Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted],
                LogLevel.Information);

        return new ProjectsDbContext(builder.Options);
    }

    private sealed class StubHrDirectory(Guid employeeId) : IHrDirectory
    {
        public Task<Guid?> ResolveEmployeeIdAsync(string appUserId, CancellationToken ct) => Task.FromResult<Guid?>(employeeId);

        public Task<EmployeeSummary?> GetAsync(Guid id, CancellationToken ct) =>
            throw new NotImplementedException("Not used by ProjectAccessGuard.");

        public Task<IReadOnlyList<EmployeeSummary>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            throw new NotImplementedException("Not used by ProjectAccessGuard.");

        public Task<IReadOnlyList<Guid>> DepartmentMembersAsync(Guid departmentId, CancellationToken ct) =>
            throw new NotImplementedException("Not used by ProjectAccessGuard.");

        public Task<IReadOnlyList<EmployeeProfile>> GetProfilesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            throw new NotImplementedException("Not used by ProjectAccessGuard.");
    }

    private sealed class StubCurrentUser(bool hasReadAll) : ICurrentUser
    {
        public string? UserId => "stub-user";
        public bool IsAuthenticated => true;
        public bool HasPermission(string permission) => hasReadAll && permission == ProjectsPermissions.Projects.ReadAll;
    }
}
