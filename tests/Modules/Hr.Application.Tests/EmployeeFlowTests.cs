using BuildingBlocks.Messaging;
using Hr.Contracts.Employees;
using Hr.Domain;
using Hr.Infrastructure.Persistence;
using Identity.Contracts;
using Identity.Contracts.Auth;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hr.Application.Tests;

[Collection(HrApplicationCollection.Name)]
public sealed class EmployeeFlowTests(HrApplicationFixture fixture)
{
    [Fact]
    public async Task CreateEmployee_WithUnknownAppUserId_IsRejected()
    {
        await using var scope = fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateEmployeeCommand, Guid>>();

        var result = await handler.HandleAsync(new CreateEmployeeCommand(
            AppUserId: Guid.NewGuid().ToString(), JobTitle: "Engineer",
            HireDate: DateOnly.FromDateTime(DateTime.UtcNow), ContractType: "FullTime"));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Create_then_get_then_update_then_list_roundtrips()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var appUserId = await RegisterUserAsync(sp);

        var create = sp.GetRequiredService<ICommandHandler<CreateEmployeeCommand, Guid>>();
        var createResult = await create.HandleAsync(new CreateEmployeeCommand(
            appUserId, "Engineer", DateOnly.FromDateTime(DateTime.UtcNow), "FullTime"));
        Assert.True(createResult.IsSuccess);
        var employeeId = createResult.Value;

        var get = sp.GetRequiredService<IQueryHandler<GetEmployeeQuery, EmployeeResponse>>();
        var getResult = await get.HandleAsync(new GetEmployeeQuery(employeeId));
        Assert.True(getResult.IsSuccess);
        Assert.Equal("Engineer", getResult.Value!.JobTitle);
        Assert.Equal(appUserId, getResult.Value.AppUserId);

        var update = sp.GetRequiredService<ICommandHandler<UpdateEmployeeCommand>>();
        var updateResult = await update.HandleAsync(new UpdateEmployeeCommand(employeeId, JobTitle: "Senior Engineer"));
        Assert.True(updateResult.IsSuccess);

        var afterUpdate = await get.HandleAsync(new GetEmployeeQuery(employeeId));
        Assert.Equal("Senior Engineer", afterUpdate.Value!.JobTitle);

        var list = sp.GetRequiredService<IQueryHandler<GetEmployeesQuery, BuildingBlocks.Pagination.PagedResult<EmployeeResponse>>>();
        var listResult = await list.HandleAsync(new GetEmployeesQuery(1, 50, "Senior"));
        Assert.True(listResult.IsSuccess);
        Assert.Contains(listResult.Value!.Items, e => e.Id == employeeId);
    }

    [Fact]
    public async Task AssignEmployeeToDepartment_then_reassign_is_rejected_as_duplicate()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var appUserId = await RegisterUserAsync(sp);

        var create = sp.GetRequiredService<ICommandHandler<CreateEmployeeCommand, Guid>>();
        var employeeId = (await create.HandleAsync(
            new CreateEmployeeCommand(appUserId, "Engineer", DateOnly.FromDateTime(DateTime.UtcNow), "FullTime"))).Value;

        var db = sp.GetRequiredService<HrDbContext>();
        var department = Department.Create($"Dept-{Guid.NewGuid():N}");
        db.Departments.Add(department);
        await db.SaveChangesAsync();

        var assign = sp.GetRequiredService<ICommandHandler<AssignEmployeeToDepartmentCommand>>();
        var firstAssign = await assign.HandleAsync(new AssignEmployeeToDepartmentCommand(employeeId, department.Id));
        Assert.True(firstAssign.IsSuccess);

        var secondAssign = await assign.HandleAsync(new AssignEmployeeToDepartmentCommand(employeeId, department.Id));
        Assert.True(secondAssign.IsFailure);
    }

    [Fact]
    public async Task Deactivate_employee_disables_the_login_once_the_outbox_is_processed()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var appUserId = await RegisterUserAsync(sp);

        var create = sp.GetRequiredService<ICommandHandler<CreateEmployeeCommand, Guid>>();
        var employeeId = (await create.HandleAsync(
            new CreateEmployeeCommand(appUserId, "Engineer", DateOnly.FromDateTime(DateTime.UtcNow), "FullTime"))).Value;

        var userDirectory = sp.GetRequiredService<IUserDirectory>();
        var beforeDeactivate = await userDirectory.GetAsync(appUserId, CancellationToken.None);
        Assert.True(beforeDeactivate!.IsActive);

        var deactivate = sp.GetRequiredService<ICommandHandler<DeactivateEmployeeCommand>>();
        var deactivateResult = await deactivate.HandleAsync(new DeactivateEmployeeCommand(employeeId));
        Assert.True(deactivateResult.IsSuccess);

        // The employee row flips immediately; the login is disabled only once the outbox delivers the
        // EmployeeDeactivatedIntegrationEvent - driving the processor directly here rather than waiting
        // for its real poll interval.
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);

        var afterDeactivate = await userDirectory.GetAsync(appUserId, CancellationToken.None);
        Assert.False(afterDeactivate!.IsActive);

        // A second run must be a safe no-op, not a duplicate/failing call - UserAdministration.DeactivateAsync
        // is idempotent, and the outbox message is already marked processed.
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);
        var stillInactive = await userDirectory.GetAsync(appUserId, CancellationToken.None);
        Assert.False(stillInactive!.IsActive);
    }

    /// <summary>Registers a real Identity user via the real command handler, in the seeded "employee"
    /// role (seeded by <c>HrApplicationFixture</c> through the public <c>IModule.SeedAsync</c> - this
    /// project has no way to construct a role directly, correctly, since that would need an
    /// InternalsVisibleTo grant across the module boundary).</summary>
    private static async Task<string> RegisterUserAsync(IServiceProvider sp)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"employee-{suffix}@example.com";

        var register = sp.GetRequiredService<ICommandHandler<RegisterCommand, string>>();
        var result = await register.HandleAsync(
            new RegisterCommand(new RegisterRequest(email, "P@ssw0rd123!", "Test", "User", null, "employee")));

        Assert.True(result.IsSuccess);
        return result.Value!;
    }
}
