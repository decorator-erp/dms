using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Hr.Contracts;
using Hr.Contracts.Employees;
using Hr.Domain;
using Hr.Infrastructure.Persistence;
using Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Hr.Application.Tests;

[Collection(HrApplicationCollection.Name)]
public sealed class EmployeeFlowTests(HrApplicationFixture fixture)
{
    /// <summary>
    /// <b>The ADR's central invariant.</b> Hiring provisions the login, but leaves it unusable until the
    /// employee row has committed and its integration event has been delivered.
    ///
    /// <para>The pre-drain assertions are the load-bearing half. The mutation they exist to catch is an
    /// inline <c>await userAdministration.ReactivateAsync(appUserId, ct);</c> added to
    /// <c>CreateEmployeeCommandHandler</c> before its <c>SaveChangesAsync</c> — the obvious way to
    /// "avoid the outbox delay", and the one that reopens the hole: it makes the account usable before
    /// anything guarantees a person is attached to it. The post-drain assertions are the positive
    /// control, so a failure to activate at all cannot pass as success.</para>
    /// </summary>
    [Fact]
    public async Task Hired_employees_login_is_inactive_until_the_outbox_delivers_the_hire_event()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        string email = $"hire-gate-{Guid.NewGuid():N}@example.com";

        Result<Guid> hired = await HireAsync(sp, email);
        Assert.True(hired.IsSuccess, hired.IsFailure ? hired.Error.ToString() : null);

        IHrDirectory hrDirectory = sp.GetRequiredService<IHrDirectory>();
        EmployeeSummary employee = (await hrDirectory.GetAsync(hired.Value, CancellationToken.None))!;
        string appUserId = employee.AppUserId;

        IUserDirectory userDirectory = sp.GetRequiredService<IUserDirectory>();

        // BEFORE the outbox runs: the person exists, the login does not work.
        UserSummary? beforeDrain = await userDirectory.GetAsync(appUserId, CancellationToken.None);
        Assert.NotNull(beforeDrain);
        Assert.False(
            beforeDrain!.IsActive,
            "A newly hired employee's login was already active before EmployeeHiredIntegrationEvent was "
            + "delivered. CreateEmployeeCommandHandler must not activate inline - the gap between "
            + "provisioning and activation is what stops a half-finished hire leaving a usable account.");

        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);

        UserSummary? afterDrain = await userDirectory.GetAsync(appUserId, CancellationToken.None);
        Assert.True(afterDrain!.IsActive, "The hire event was delivered but the login was never activated.");
    }

    /// <summary>
    /// The crash window, asserted from the other side: this is the state the system is left in if the
    /// process dies after <c>IUserProvisioning.CreateAsync</c> and before <c>SaveChangesAsync</c>. The
    /// account exists and is inert — no employee record, and inactive. Step F's reaper is what
    /// eventually collects it.
    /// </summary>
    [Fact]
    public async Task A_provisioned_user_with_no_employee_row_is_inactive_and_unlinked()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        Result<string> provisioned = await sp.GetRequiredService<IUserProvisioning>().CreateAsync(
            new NewUserRequest(
                $"orphan-{Guid.NewGuid():N}@example.com", IdentityTestSeeding.Password,
                "Half", "Hire", null, UserKind.Employee, IdentityRoles.Employee),
            CancellationToken.None);
        Assert.True(provisioned.IsSuccess, provisioned.IsFailure ? provisioned.Error.ToString() : null);

        UserSummary? user = await sp.GetRequiredService<IUserDirectory>()
            .GetAsync(provisioned.Value!, CancellationToken.None);
        Assert.False(user!.IsActive);

        Assert.Null(await sp.GetRequiredService<IHrDirectory>()
            .ResolveEmployeeIdAsync(provisioned.Value!, CancellationToken.None));
    }

    /// <summary>A hire that fails after the login was provisioned must not leave that login usable. The
    /// duplicate email is the failure this path can actually produce.</summary>
    [Fact]
    public async Task A_hire_that_fails_leaves_no_usable_login()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        string email = $"dup-hire-{Guid.NewGuid():N}@example.com";

        Result<Guid> first = await HireAsync(sp, email);
        Assert.True(first.IsSuccess, first.IsFailure ? first.Error.ToString() : null);

        Result<Guid> second = await HireAsync(sp, email);

        // The exact error code is asserted in UserProvisioningTests, inside Identity, where UserErrors
        // is reachable - it is internal to Identity.Domain and rightly not visible from here.
        Assert.True(second.IsFailure, "A second hire reusing the same email address was accepted.");

        // Exactly one employee holds that login - the failed attempt added nothing.
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);
        EmployeeSummary employee = (await sp.GetRequiredService<IHrDirectory>()
            .GetAsync(first.Value, CancellationToken.None))!;
        Assert.Equal(
            first.Value,
            await sp.GetRequiredService<IHrDirectory>()
                .ResolveEmployeeIdAsync(employee.AppUserId, CancellationToken.None));
    }

    // No test here that a privileged role is unreachable through hiring: CreateEmployeeCommand has no
    // role field at all, so it is a compile-time property rather than a runtime one. The runtime half -
    // that IUserProvisioning refuses every DefaultRoles.FullAccessRoles name, and that the role actually
    // granted is IdentityRoles.Employee - is asserted in UserProvisioningTests, inside Identity, where
    // the roles are inspectable.

    /// <summary>Finding 2.5: <c>Enum.TryParse</c> alone accepts any numeric string, so <c>"99"</c> used
    /// to parse into an undefined <c>ContractType</c> and be written to the row.</summary>
    [Fact]
    public async Task ContractType_that_is_a_number_is_rejected()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        Result<Guid> result = await sp.GetRequiredService<ICommandHandler<CreateEmployeeCommand, Guid>>()
            .HandleAsync(new CreateEmployeeCommand(
                $"contract-{Guid.NewGuid():N}@example.com", IdentityTestSeeding.Password,
                "Test", "User", "Engineer", DateOnly.FromDateTime(DateTime.UtcNow), "99"));

        Assert.True(result.IsFailure, "\"99\" was accepted as a contract type.");

        // POSITIVE CONTROL - the same command with a real contract type succeeds, so the rejection above
        // is the Enum.IsDefined check and not something else failing first.
        Result<Guid> valid = await HireAsync(sp, $"contract-ok-{Guid.NewGuid():N}@example.com");
        Assert.True(valid.IsSuccess, valid.IsFailure ? valid.Error.ToString() : null);
    }

    [Fact]
    public async Task Create_then_get_then_update_then_list_roundtrips()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        (Guid employeeId, string appUserId) = await HrTestSeeding.HireEmployeeAsync(
            sp, $"employee-{Guid.NewGuid():N}@example.com");

        IQueryHandler<GetEmployeeQuery, EmployeeResponse> get = sp.GetRequiredService<IQueryHandler<GetEmployeeQuery, EmployeeResponse>>();
        Result<EmployeeResponse> getResult = await get.HandleAsync(new GetEmployeeQuery(employeeId));
        Assert.True(getResult.IsSuccess);
        Assert.Equal("Engineer", getResult.Value!.JobTitle);
        Assert.Equal(appUserId, getResult.Value.AppUserId);

        ICommandHandler<UpdateEmployeeCommand> update = sp.GetRequiredService<ICommandHandler<UpdateEmployeeCommand>>();
        Result updateResult = await update.HandleAsync(new UpdateEmployeeCommand(employeeId, JobTitle: "Senior Engineer"));
        Assert.True(updateResult.IsSuccess);

        Result<EmployeeResponse> afterUpdate = await get.HandleAsync(new GetEmployeeQuery(employeeId));
        Assert.Equal("Senior Engineer", afterUpdate.Value!.JobTitle);

        IQueryHandler<GetEmployeesQuery, PagedResult<EmployeeResponse>> list = sp.GetRequiredService<IQueryHandler<GetEmployeesQuery, PagedResult<EmployeeResponse>>>();
        Result<PagedResult<EmployeeResponse>> listResult = await list.HandleAsync(new GetEmployeesQuery(1, 50, "Senior"));
        Assert.True(listResult.IsSuccess);
        Assert.Contains(listResult.Value!.Items, e => e.Id == employeeId);
    }

    [Fact]
    public async Task AssignEmployeeToDepartment_then_reassign_is_rejected_as_duplicate()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        (Guid employeeId, _) = await HrTestSeeding.HireEmployeeAsync(
            sp, $"employee-{Guid.NewGuid():N}@example.com");

        HrDbContext db = sp.GetRequiredService<HrDbContext>();
        var department = Department.Create($"Dept-{Guid.NewGuid():N}");
        db.Departments.Add(department);
        await db.SaveChangesAsync();

        ICommandHandler<AssignEmployeeToDepartmentCommand> assign = sp.GetRequiredService<ICommandHandler<AssignEmployeeToDepartmentCommand>>();
        Result firstAssign = await assign.HandleAsync(new AssignEmployeeToDepartmentCommand(employeeId, department.Id));
        Assert.True(firstAssign.IsSuccess);

        Result secondAssign = await assign.HandleAsync(new AssignEmployeeToDepartmentCommand(employeeId, department.Id));
        Assert.True(secondAssign.IsFailure);
    }

    [Fact]
    public async Task Deactivate_employee_disables_the_login_once_the_outbox_is_processed()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        (Guid employeeId, string appUserId) = await HrTestSeeding.HireEmployeeAsync(
            sp, $"employee-{Guid.NewGuid():N}@example.com");

        IUserDirectory userDirectory = sp.GetRequiredService<IUserDirectory>();
        UserSummary? beforeDeactivate = await userDirectory.GetAsync(appUserId, CancellationToken.None);
        Assert.True(beforeDeactivate!.IsActive);

        ICommandHandler<DeactivateEmployeeCommand> deactivate = sp.GetRequiredService<ICommandHandler<DeactivateEmployeeCommand>>();
        Result deactivateResult = await deactivate.HandleAsync(new DeactivateEmployeeCommand(employeeId));
        Assert.True(deactivateResult.IsSuccess);

        // The employee row flips immediately; the login is disabled only once the outbox delivers the
        // EmployeeDeactivatedIntegrationEvent - driving the processor directly here rather than waiting
        // for its real poll interval.
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);

        UserSummary? afterDeactivate = await userDirectory.GetAsync(appUserId, CancellationToken.None);
        Assert.False(afterDeactivate!.IsActive);

        // A second run must be a safe no-op, not a duplicate/failing call - UserAdministration.DeactivateAsync
        // is idempotent, and the outbox message is already marked processed.
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);
        UserSummary? stillInactive = await userDirectory.GetAsync(appUserId, CancellationToken.None);
        Assert.False(stillInactive!.IsActive);
    }

    /// <summary>The command under test, with only the email varying. Deliberately NOT
    /// <c>HrTestSeeding.HireEmployeeAsync</c>: that helper activates the login and asserts success,
    /// both of which several tests here exist to examine for themselves.</summary>
    private static Task<Result<Guid>> HireAsync(IServiceProvider sp, string email) =>
        sp.GetRequiredService<ICommandHandler<CreateEmployeeCommand, Guid>>()
            .HandleAsync(new CreateEmployeeCommand(
                email, IdentityTestSeeding.Password, "Test", "User", "Engineer",
                DateOnly.FromDateTime(DateTime.UtcNow), "FullTime"));
}
