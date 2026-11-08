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
using Projects.Domain;
using Projects.Infrastructure.Persistence;
using Projects.Infrastructure.Services;
using SharedKernel;
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
        _container = new PostgreSqlBuilder("postgres:17").Build();
        await _container.StartAsync();

        // projects.projects."OwnerId" -> hr.employees, hr.employees."AppUserId" -> identity.users,
        // projects.gate_conditions."ApprovalId" -> approvals.approvals, projects.projects."ClientId" ->
        // crm.clients (G9-13) - all four must exist before ProjectsDbContext's own migration (with its
        // raw-SQL FKs) can apply.
        await MigrateModuleAsync(
            "Identity.Infrastructure", "Identity.Infrastructure.Persistence.IdentityModuleDbContext, Identity.Infrastructure");
        await MigrateModuleAsync("Hr.Infrastructure", "Hr.Infrastructure.Persistence.HrDbContext, Hr.Infrastructure");
        await MigrateModuleAsync(
            "Approvals.Infrastructure", "Approvals.Infrastructure.Persistence.ApprovalsDbContext, Approvals.Infrastructure");
        await MigrateModuleAsync("Crm.Infrastructure", "Crm.Infrastructure.Persistence.CrmDbContext, Crm.Infrastructure");

        await using ProjectsDbContext db = CreateContext(_ => { });
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
    public async Task EnsureProjectAccessAsync_NoPermission_NotAMember_IsDenied()
    {
        (string? appUserId, Guid employeeId) = await SeedEmployeeAsync();
        // D-14: the project is someone else's — its owner would have access without being a member.
        (_, Guid ownerId) = await SeedEmployeeAsync();
        Guid projectId = await SeedProjectAsync(ownerId);

        await using ProjectsDbContext db = CreateContext(_ => { });
        ProjectAccessGuard guard = Guard(db, employeeId, hasReadAll: false);

        Result result = await guard.EnsureProjectAccessAsync(projectId, appUserId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(Domain.ProjectErrors.AccessDenied, result.Error);
    }

    [Fact]
    public async Task EnsureProjectAccessAsync_ReadAllPermission_IsAllowedRegardlessOfMembership()
    {
        (string? appUserId, Guid employeeId) = await SeedEmployeeAsync();
        Guid projectId = await SeedProjectAsync(employeeId);

        await using ProjectsDbContext db = CreateContext(_ => { });
        ProjectAccessGuard guard = Guard(db, employeeId, hasReadAll: true);

        Result result = await guard.EnsureProjectAccessAsync(projectId, appUserId, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task EnsureProjectAccessAsync_StageTeamMember_IsAllowed()
    {
        (string? appUserId, Guid employeeId) = await SeedEmployeeAsync();
        Guid projectId = await SeedProjectAsync(employeeId);
        await SeedStageTeamMembershipAsync(projectId, employeeId);

        await using ProjectsDbContext db = CreateContext(_ => { });
        ProjectAccessGuard guard = Guard(db, employeeId, hasReadAll: false);

        Result result = await guard.EnsureProjectAccessAsync(projectId, appUserId, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>D-14 (C-F2, owner default): the project's owner has access with no stage team at all, in
    /// both the single check and the batch filter.</summary>
    [Fact]
    public async Task TheOwner_WithNoTeamAndNoPermission_IsAllowed_InBothMethods()
    {
        (string? appUserId, Guid employeeId) = await SeedEmployeeAsync();
        Guid projectId = await SeedProjectAsync(employeeId);

        await using ProjectsDbContext db = CreateContext(_ => { });
        ProjectAccessGuard guard = Guard(db, employeeId, hasReadAll: false);

        Assert.True((await guard.EnsureProjectAccessAsync(projectId, appUserId, CancellationToken.None)).IsSuccess);
        Assert.Equal([projectId], await guard.FilterAccessibleProjectsAsync([projectId], appUserId, CancellationToken.None));
    }

    [Fact]
    public async Task FilterAccessibleProjectsAsync_Over50Projects_IssuesExactlyOneMembershipQuery()
    {
        (string? appUserId, Guid employeeId) = await SeedEmployeeAsync();
        var accessibleIds = new List<Guid>();
        for (int i = 0; i < 50; i++)
        {
            Guid projectId = await SeedProjectAsync(employeeId);
            await SeedStageTeamMembershipAsync(projectId, employeeId);
            accessibleIds.Add(projectId);
        }

        // D-14: owned by someone else, so the caller reaches it neither as owner nor as member.
        (_, Guid otherOwnerId) = await SeedEmployeeAsync();
        Guid inaccessibleId = await SeedProjectAsync(otherOwnerId);

        int commandCount = 0;
        await using ProjectsDbContext db = CreateContext(_ => Interlocked.Increment(ref commandCount));
        ProjectAccessGuard guard = Guard(db, employeeId, hasReadAll: false);

        var allIds = new List<Guid>(accessibleIds) { inaccessibleId };
        IReadOnlyList<Guid> result = await guard.FilterAccessibleProjectsAsync(allIds, appUserId, CancellationToken.None);

        Assert.Equal(50, result.Count);
        Assert.DoesNotContain(inaccessibleId, result);
        Assert.Equal(1, commandCount);
    }

    [Fact]
    public async Task FilterAccessibleProjectsAsync_EmptyList_ReturnsEmpty_NoQuery()
    {
        int commandCount = 0;
        await using ProjectsDbContext db = CreateContext(_ => Interlocked.Increment(ref commandCount));
        ProjectAccessGuard guard = Guard(db, Guid.NewGuid(), hasReadAll: false);

        IReadOnlyList<Guid> result = await guard.FilterAccessibleProjectsAsync([], "some-user", CancellationToken.None);

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
        string appUserId = $"user-{Guid.NewGuid():N}";
        var employeeId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();

        await using (NpgsqlCommand userCommand = connection.CreateCommand())
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

        await using (NpgsqlCommand employeeCommand = connection.CreateCommand())
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
        Guid clientId = await SeedClientAsync();
        var project = Domain.Project.Create(
            $"P-{Guid.NewGuid():N}", "Test Project", "123 Main St", Domain.WorkflowType.Stage, clientId, ownerId);

        await using ProjectsDbContext db = CreateContext(_ => { });
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        return project.Id;
    }

    /// <summary>G9-13: <c>projects.projects."ClientId"</c> now carries a real FK to <c>crm.clients</c>,
    /// so every seeded project needs a real client row - raw SQL, same reasoning as
    /// <see cref="SeedEmployeeAsync"/> (no <c>InternalsVisibleTo</c> grant to <c>Crm.Domain</c> from
    /// this test project, correctly; it isn't part of CRM). A fresh <c>identity.users</c> row backs the
    /// client's own <c>AppUserId</c> FK (G9-08) rather than reusing the employee's - this guard's own
    /// tests only ever assert on <c>Project</c>/<c>StageTeam</c> state, so a client sharing an identity
    /// with its project's employee would prove nothing extra and cost an unnecessary parameter thread.</summary>
    private async Task<Guid> SeedClientAsync()
    {
        string clientAppUserId = $"client-{Guid.NewGuid():N}";
        var clientId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();

        await using (NpgsqlCommand userCommand = connection.CreateCommand())
        {
            userCommand.CommandText = """
                INSERT INTO identity.users
                    ("Id", "FirstName", "LastName", "IsActive", "UserType", "CreatedAt",
                     "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
                VALUES (@id, 'Test', 'Client', true, 0, now(), true, false, false, false, 0);
                """;
            userCommand.Parameters.AddWithValue("id", clientAppUserId);
            await userCommand.ExecuteNonQueryAsync();
        }

        await using (NpgsqlCommand clientCommand = connection.CreateCommand())
        {
            clientCommand.CommandText = """
                INSERT INTO crm.clients ("Id", "AppUserId", "Address", "IsActive", "CreatedAtUtc")
                VALUES (@id, @appUserId, '1 Client Rd', true, now());
                """;
            clientCommand.Parameters.AddWithValue("id", clientId);
            clientCommand.Parameters.AddWithValue("appUserId", clientAppUserId);
            await clientCommand.ExecuteNonQueryAsync();
        }

        return clientId;
    }

    private async Task SeedStageTeamMembershipAsync(Guid projectId, Guid employeeId)
    {
        await using ProjectsDbContext db = CreateContext(_ => { });

        var stage = Domain.ProjectStage.Create("Design", 1, projectId);
        db.ProjectStages.Add(stage);
        await db.SaveChangesAsync();

        var team = Domain.StageTeam.CreateProjectStandaloneTeam(stage.Id, employeeId);
        Result<StageTeamMember> addResult = team.AddMember(employeeId, Domain.StageTeamMemberRole.Responsible);
        Assert.True(addResult.IsSuccess);
        db.StageTeams.Add(team);
        await db.SaveChangesAsync();
    }

    private ProjectsDbContext CreateContext(Action<string> onCommandExecuted)
    {
        DbContextOptionsBuilder<ProjectsDbContext> builder = new DbContextOptionsBuilder<ProjectsDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .LogTo(
                message => onCommandExecuted(message),
                [Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted],
                LogLevel.Information);

        return new ProjectsDbContext(builder.Options);
    }

    /// <summary>P1-A: the guard reads its sources through <see cref="ProjectAccessContext"/>, on the system clock, with
    /// no implied permissions (so never a grant-only request).</summary>
    private static ProjectAccessGuard Guard(ProjectsDbContext db, Guid employeeId, bool hasReadAll) =>
        new(new ProjectAccessContext(
            db, new StubHrDirectory(employeeId), new StubCurrentUser(hasReadAll), TimeProvider.System, new ImpliedPermissions([])));

    private sealed class StubHrDirectory(Guid employeeId) : IHrDirectory
    {
        public Task<Guid?> ResolveEmployeeIdAsync(string appUserId, CancellationToken ct) => Task.FromResult<Guid?>(employeeId);

        public Task<EmployeeSummary?> GetAsync(Guid id, CancellationToken ct) =>
            throw new NotImplementedException("Not used by ProjectAccessGuard.");

        public Task<IReadOnlyList<EmployeeSummary>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            throw new NotImplementedException("Not used by ProjectAccessGuard.");

        public Task<IReadOnlyList<Guid>> DepartmentMembersAsync(Guid departmentId, CancellationToken ct) =>
            throw new NotImplementedException("Not used by ProjectAccessGuard.");

        public Task<IReadOnlyList<DepartmentHeadSummary>> GetDepartmentHeadsAsync(IReadOnlyCollection<Guid> departmentIds, CancellationToken ct) =>
            throw new NotImplementedException("Not used by ProjectAccessGuard.");

        public Task<IReadOnlyList<EmployeeProfile>> GetProfilesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            throw new NotImplementedException("Not used by ProjectAccessGuard.");

        public Task<bool> IsLinkedAsync(string appUserId, CancellationToken ct) =>
            throw new NotImplementedException("Not used by ProjectAccessGuard.");

        public Task<IReadOnlyCollection<string>> GetLinkedAsync(IReadOnlyCollection<string> appUserIds, CancellationToken ct) =>
            throw new NotImplementedException("Not used by ProjectAccessGuard.");
    }

    private sealed class StubCurrentUser(bool hasReadAll) : ICurrentUser
    {
        public string? UserId => "stub-user";
        public bool IsAuthenticated => true;
        public bool HasPermission(string permission) => hasReadAll && permission == ProjectsPermissions.Projects.ReadAll;
    }
}
