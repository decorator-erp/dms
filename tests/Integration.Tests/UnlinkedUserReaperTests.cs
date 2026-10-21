using System.Globalization;
using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Hr.Contracts.Employees;
using Identity.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// The reaper collects logins that were provisioned and never claimed, and nothing else.
///
/// <para><b>Time is controlled through configuration, not the clock.</b> The retention threshold is
/// <c>Provisioning:UnlinkedUserRetentionDays</c>; setting it to zero makes a just-created account
/// instantly eligible, which is both simpler and safer than the two alternatives — faking
/// <c>TimeProvider</c> would move the clock for every module in the shared host, and back-dating
/// <c>identity.users."CreatedAt"</c> with raw SQL would have this test writing into another module's
/// schema, which is the thing the schema-isolation tests exist to catch. It also means the config path
/// itself is exercised rather than assumed.</para>
///
/// <para>Runs against the real Api host because the job lives in <c>Api.Jobs</c> and is registered in
/// <c>Api.DI</c> — the module-level fixtures never call <c>AddApi</c> and cannot see it. It is resolved
/// by <c>Name</c> rather than by type for the same reason <c>PeriodicJobTests</c> does: the job is
/// <c>internal</c> to Api, and this project has no <c>InternalsVisibleTo</c> grant.</para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class UnlinkedUserReaperTests(ApiFixture fixture)
{
    private const string RetentionDaysKey = "Provisioning:UnlinkedUserRetentionDays";

    [Fact]
    public async Task An_inactive_unlinked_user_past_the_threshold_is_reaped()
    {
        string userId = await ProvisionUnlinkedAsync("reap-me");

        await RunReaperAsync(retentionDays: 0);

        Assert.Null(await GetUserAsync(userId));
    }

    /// <summary>
    /// The distinction the whole job turns on. A deactivated ex-employee is inactive and old, exactly
    /// like the account above — the only thing separating them is that something still points at this
    /// one. Deleting it would destroy the audit trail's readability for everything that person ever did.
    /// </summary>
    [Fact]
    public async Task An_inactive_user_who_has_an_employee_record_is_never_reaped()
    {
        (_, string appUserId) = await HrTestSeeding.HireEmployeeAsync(
            fixture, $"linked-{Guid.NewGuid():N}@example.com");

        // Deactivated, so IsActive alone cannot tell this account apart from an abandoned one.
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            Result deactivated = await scope.ServiceProvider.GetRequiredService<IUserAdministration>()
                .DeactivateAsync(appUserId, CancellationToken.None);
            Assert.True(deactivated.IsSuccess);
        }

        await RunReaperAsync(retentionDays: 0);

        UserSummary? survivor = await GetUserAsync(appUserId);
        Assert.NotNull(survivor);
        Assert.False(survivor!.IsActive);
    }

    /// <summary>
    /// HR Phase 2 (A1): the directory hides inactive employees now, so "is linked" can no longer be the
    /// same question as "resolves to an employee". Here the EMPLOYEE is deactivated through HR, and the
    /// outbox deactivates the login, so both are inactive and the filtered directory sees no employee.
    /// The reaper must still keep the login: <c>IsLinkedAsync</c> counts inactive employees.
    /// </summary>
    [Fact]
    public async Task A_deactivated_employees_login_is_never_reaped()
    {
        (Guid employeeId, string appUserId) = await HrTestSeeding.HireEmployeeAsync(
            fixture, $"left-{Guid.NewGuid():N}@example.com");

        OutboxProcessor outbox = fixture.Services.GetServices<IHostedService>().OfType<OutboxProcessor>().Single();
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            Result deactivated = await scope.ServiceProvider.GetRequiredService<ICommandHandler<DeactivateEmployeeCommand>>()
                .HandleAsync(new DeactivateEmployeeCommand(employeeId));
            Assert.True(deactivated.IsSuccess, deactivated.IsFailure ? deactivated.Error.ToString() : null);
        }

        await outbox.ProcessPendingAsync(CancellationToken.None);
        Assert.False((await GetUserAsync(appUserId))!.IsActive);

        await RunReaperAsync(retentionDays: 0);

        Assert.NotNull(await GetUserAsync(appUserId));
    }

    [Fact]
    public async Task A_recently_provisioned_user_is_not_reaped()
    {
        string userId = await ProvisionUnlinkedAsync("too-new");

        // The configured default, against which a just-created account is nowhere near eligible.
        await RunReaperAsync(retentionDays: 7);

        Assert.NotNull(await GetUserAsync(userId));
    }

    /// <summary>An active account is never housekeeping, whatever else is true of it — including the
    /// seeded administrator, which is active AND deliberately linked to nothing.</summary>
    [Fact]
    public async Task Active_users_and_the_seeded_admin_are_never_reaped()
    {
        (_, string activeLinked) = await HrTestSeeding.HireEmployeeAsync(
            fixture, $"active-{Guid.NewGuid():N}@example.com");

        const string seededAdminId = "00000000-0000-0000-0000-000000000001";

        await RunReaperAsync(retentionDays: 0);

        Assert.NotNull(await GetUserAsync(activeLinked));

        // PF-S1: ApiFixture always supplies DMS_SEED_ADMIN_PASSWORD, so the seeded admin always exists.
        Assert.NotNull(await GetUserAsync(seededAdminId));
        await RunReaperAsync(retentionDays: 0);
        Assert.NotNull(await GetUserAsync(seededAdminId));

        // Direct refusal, independent of whether the sweep ever offered it: DeleteUnactivatedAsync must
        // say no to the seed admin id by name.
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        Result refused = await scope.ServiceProvider.GetRequiredService<IUserLifecycleMaintenance>()
            .DeleteUnactivatedAsync(seededAdminId, CancellationToken.None);
        Assert.True(refused.IsFailure, "DeleteUnactivatedAsync accepted the seeded administrator's id.");
    }

    /// <summary>The batch limit is real, not decorative — it is what bounds both the query and the two
    /// per-candidate directory calls the job makes for each result.</summary>
    [Fact]
    public async Task ListUnactivated_returns_no_more_than_the_limit()
    {
        for (int i = 0; i < 3; i++)
        {
            await ProvisionUnlinkedAsync($"batch-{i}");
        }

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IUserLifecycleMaintenance maintenance = scope.ServiceProvider.GetRequiredService<IUserLifecycleMaintenance>();

        IReadOnlyList<string> capped = await maintenance.ListUnactivatedAsync(
            DateTime.UtcNow.AddDays(1), limit: 2, CancellationToken.None);
        Assert.Equal(2, capped.Count);

        // POSITIVE CONTROL - the same call with room returns more, so the cap above is the Take() and
        // not simply all there was.
        IReadOnlyList<string> uncapped = await maintenance.ListUnactivatedAsync(
            DateTime.UtcNow.AddDays(1), limit: 50, CancellationToken.None);
        Assert.True(uncapped.Count >= 3, $"Expected at least 3 unactivated users, found {uncapped.Count}.");
    }

    /// <summary>Provisions a login and leaves it exactly as a half-finished hire would: inactive, with
    /// no employee and no client pointing at it.</summary>
    private async Task<string> ProvisionUnlinkedAsync(string prefix)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();

        Result<string> provisioned = await scope.ServiceProvider.GetRequiredService<IUserProvisioning>()
            .CreateAsync(
                new NewUserRequest(
                    $"{prefix}-{Guid.NewGuid():N}@example.com", IdentityTestSeeding.Password,
                    "Un", "Linked", null, UserKind.Employee, IdentityRoles.Employee),
                CancellationToken.None);

        Assert.True(provisioned.IsSuccess, provisioned.IsFailure ? provisioned.Error.ToString() : null);
        return provisioned.Value!;
    }

    private async Task<UserSummary?> GetUserAsync(string userId)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IUserDirectory>()
            .GetAsync(userId, CancellationToken.None);
    }

    /// <summary>
    /// Runs the reaper with a chosen retention threshold, restoring the host's own value afterwards.
    /// Mutating shared configuration is safe here only because xUnit runs a collection's tests
    /// sequentially — if this class ever leaves <see cref="ApiCollection"/>, this has to change.
    /// </summary>
    private async Task RunReaperAsync(int retentionDays)
    {
        IConfiguration configuration = fixture.Services.GetRequiredService<IConfiguration>();
        string? original = configuration[RetentionDaysKey];

        configuration[RetentionDaysKey] = retentionDays.ToString(CultureInfo.InvariantCulture);
        try
        {
            await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();

            IPeriodicJob reaper = scope.ServiceProvider.GetServices<IPeriodicJob>()
                .Single(job => job.Name == "UnlinkedUserReaperJob");

            await reaper.RunAsync(CancellationToken.None);
        }
        finally
        {
            configuration[RetentionDaysKey] = original;
        }
    }
}
