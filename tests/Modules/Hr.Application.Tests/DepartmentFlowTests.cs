using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Hr.Contracts.Departments;
using Hr.Contracts.Employees;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hr.Application.Tests;

[Collection(HrApplicationCollection.Name)]
public sealed class DepartmentFlowTests(HrApplicationFixture fixture)
{
    [Fact]
    public async Task Create_then_get_then_update_then_list_roundtrips()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var name = $"Engineering-{Guid.NewGuid():N}";

        var create = sp.GetRequiredService<ICommandHandler<CreateDepartmentCommand, Guid>>();
        var createResult = await create.HandleAsync(new CreateDepartmentCommand(name, "Builds the product"));
        Assert.True(createResult.IsSuccess);
        var departmentId = createResult.Value;

        var get = sp.GetRequiredService<IQueryHandler<GetDepartmentQuery, DepartmentResponse>>();
        var getResult = await get.HandleAsync(new GetDepartmentQuery(departmentId));
        Assert.True(getResult.IsSuccess);
        Assert.Equal(name, getResult.Value!.Name);

        var update = sp.GetRequiredService<ICommandHandler<UpdateDepartmentCommand>>();
        var updateResult = await update.HandleAsync(new UpdateDepartmentCommand(departmentId, name, "New description"));
        Assert.True(updateResult.IsSuccess);

        var afterUpdate = await get.HandleAsync(new GetDepartmentQuery(departmentId));
        Assert.Equal("New description", afterUpdate.Value!.Description);

        var list = sp.GetRequiredService<IQueryHandler<GetDepartmentsQuery, PagedResult<DepartmentResponse>>>();
        var listResult = await list.HandleAsync(new GetDepartmentsQuery(1, 50, name));
        Assert.Contains(listResult.Value!.Items, d => d.Id == departmentId);
    }

    [Fact]
    public async Task Create_department_with_duplicate_name_is_rejected()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var name = $"Dept-{Guid.NewGuid():N}";

        var create = sp.GetRequiredService<ICommandHandler<CreateDepartmentCommand, Guid>>();
        var first = await create.HandleAsync(new CreateDepartmentCommand(name));
        Assert.True(first.IsSuccess);

        var second = await create.HandleAsync(new CreateDepartmentCommand(name));
        Assert.True(second.IsFailure);
    }

    [Fact]
    public async Task Delete_department_deactivates_it()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;

        var create = sp.GetRequiredService<ICommandHandler<CreateDepartmentCommand, Guid>>();
        var departmentId = (await create.HandleAsync(new CreateDepartmentCommand($"Dept-{Guid.NewGuid():N}"))).Value;

        var delete = sp.GetRequiredService<ICommandHandler<DeleteDepartmentCommand>>();
        var deleteResult = await delete.HandleAsync(new DeleteDepartmentCommand(departmentId));
        Assert.True(deleteResult.IsSuccess);

        var get = sp.GetRequiredService<IQueryHandler<GetDepartmentQuery, DepartmentResponse>>();
        var afterDelete = await get.HandleAsync(new GetDepartmentQuery(departmentId));
        Assert.False(afterDelete.Value!.IsActive);
    }

    [Fact]
    public async Task AssignDepartmentHead_makes_the_employee_a_member_then_unassign_clears_it()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;

        var employeeId = await CreateEmployeeAsync(sp);

        var createDept = sp.GetRequiredService<ICommandHandler<CreateDepartmentCommand, Guid>>();
        var departmentId = (await createDept.HandleAsync(new CreateDepartmentCommand($"Dept-{Guid.NewGuid():N}"))).Value;

        var assignHead = sp.GetRequiredService<ICommandHandler<AssignDepartmentHeadCommand>>();
        var assignResult = await assignHead.HandleAsync(new AssignDepartmentHeadCommand(departmentId, employeeId));
        Assert.True(assignResult.IsSuccess);

        var get = sp.GetRequiredService<IQueryHandler<GetDepartmentQuery, DepartmentResponse>>();
        var afterAssign = await get.HandleAsync(new GetDepartmentQuery(departmentId));
        Assert.Equal(employeeId, afterAssign.Value!.DepartmentHeadId);

        // Assigning a second head while one is already assigned must be rejected.
        var otherEmployeeId = await CreateEmployeeAsync(sp);
        var secondAssign = await assignHead.HandleAsync(new AssignDepartmentHeadCommand(departmentId, otherEmployeeId));
        Assert.True(secondAssign.IsFailure);

        var unassign = sp.GetRequiredService<ICommandHandler<UnassignDepartmentHeadCommand>>();
        var unassignResult = await unassign.HandleAsync(new UnassignDepartmentHeadCommand(departmentId));
        Assert.True(unassignResult.IsSuccess);

        var afterUnassign = await get.HandleAsync(new GetDepartmentQuery(departmentId));
        Assert.Null(afterUnassign.Value!.DepartmentHeadId);
    }

    [Fact]
    public async Task MoveEmployee_between_departments_then_remove_leaves_no_active_membership()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;

        var employeeId = await CreateEmployeeAsync(sp);

        var createDept = sp.GetRequiredService<ICommandHandler<CreateDepartmentCommand, Guid>>();
        var departmentAId = (await createDept.HandleAsync(new CreateDepartmentCommand($"Dept-A-{Guid.NewGuid():N}"))).Value;
        var departmentBId = (await createDept.HandleAsync(new CreateDepartmentCommand($"Dept-B-{Guid.NewGuid():N}"))).Value;

        var assign = sp.GetRequiredService<ICommandHandler<Hr.Contracts.Employees.AssignEmployeeToDepartmentCommand>>();
        var assignResult = await assign.HandleAsync(
            new Hr.Contracts.Employees.AssignEmployeeToDepartmentCommand(employeeId, departmentAId));
        Assert.True(assignResult.IsSuccess);

        var move = sp.GetRequiredService<ICommandHandler<MoveEmployeeToDepartmentCommand>>();
        var moveResult = await move.HandleAsync(new MoveEmployeeToDepartmentCommand(employeeId, departmentBId));
        Assert.True(moveResult.IsSuccess);

        var remove = sp.GetRequiredService<ICommandHandler<RemoveEmployeeFromDepartmentCommand>>();
        var removeResult = await remove.HandleAsync(new RemoveEmployeeFromDepartmentCommand(employeeId, departmentBId));
        Assert.True(removeResult.IsSuccess);

        // No membership row at all (never assigned to a third department) is rejected - matches the
        // backend's own RemoveEmployeeAsync, which likewise checks existence without an IsActive filter,
        // so removing an ALREADY-inactive row (like departmentA's, left behind by the move above) is
        // still a no-op success, not an error - that's the backend's real behaviour, not a bug to fix.
        var otherDepartmentId = (await sp.GetRequiredService<ICommandHandler<CreateDepartmentCommand, Guid>>()
            .HandleAsync(new CreateDepartmentCommand($"Dept-C-{Guid.NewGuid():N}"))).Value;
        var removeNeverAssigned = await remove.HandleAsync(new RemoveEmployeeFromDepartmentCommand(employeeId, otherDepartmentId));
        Assert.True(removeNeverAssigned.IsFailure);
    }

    private async Task<Guid> CreateEmployeeAsync(IServiceProvider sp)
    {
        var appUserId = await RegisterUserAsync(sp);
        var create = sp.GetRequiredService<ICommandHandler<CreateEmployeeCommand, Guid>>();
        var result = await create.HandleAsync(
            new CreateEmployeeCommand(appUserId, "Engineer", DateOnly.FromDateTime(DateTime.UtcNow), "FullTime"));
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static async Task<string> RegisterUserAsync(IServiceProvider sp)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"dept-user-{suffix}@example.com";

        var register = sp.GetRequiredService<ICommandHandler<Identity.Contracts.Auth.RegisterCommand, string>>();
        var result = await register.HandleAsync(new Identity.Contracts.Auth.RegisterCommand(
            new Identity.Contracts.Auth.RegisterRequest(email, "P@ssw0rd123!", "Test", "User", null, "employee")));

        Assert.True(result.IsSuccess);
        return result.Value!;
    }
}
