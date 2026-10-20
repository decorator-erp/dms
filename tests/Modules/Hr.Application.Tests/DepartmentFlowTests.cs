using System.Globalization;
using System.Text.RegularExpressions;
using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Hr.Contracts.Departments;
using Hr.Contracts.Employees;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Hr.Application.Tests;

[Collection(HrApplicationCollection.Name)]
public sealed class DepartmentFlowTests(HrApplicationFixture fixture)
{
    [Fact]
    public async Task Create_then_get_then_update_then_list_roundtrips()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        string name = $"Engineering-{Guid.NewGuid():N}";
        name = Regex.Replace(name, @"\s+", " ");
        name = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLower());

        ICommandHandler<CreateDepartmentCommand, Guid> create = sp.GetRequiredService<ICommandHandler<CreateDepartmentCommand, Guid>>();
        Result<Guid> createResult = await create.HandleAsync(new CreateDepartmentCommand(name, "Builds the product"));
        Assert.True(createResult.IsSuccess);
        Guid departmentId = createResult.Value;

        IQueryHandler<GetDepartmentQuery, DepartmentResponse> get = sp.GetRequiredService<IQueryHandler<GetDepartmentQuery, DepartmentResponse>>();
        Result<DepartmentResponse> getResult = await get.HandleAsync(new GetDepartmentQuery(departmentId));
        Assert.True(getResult.IsSuccess);
        Assert.Equal(name, getResult.Value!.Name);

        ICommandHandler<UpdateDepartmentCommand> update = sp.GetRequiredService<ICommandHandler<UpdateDepartmentCommand>>();
        Result updateResult = await update.HandleAsync(new UpdateDepartmentCommand(departmentId, name, "New description"));
        Assert.True(updateResult.IsSuccess);

        Result<DepartmentResponse> afterUpdate = await get.HandleAsync(new GetDepartmentQuery(departmentId));
        Assert.Equal("New description", afterUpdate.Value!.Description);

        IQueryHandler<GetDepartmentsQuery, PagedResult<DepartmentResponse>> list = sp.GetRequiredService<IQueryHandler<GetDepartmentsQuery, PagedResult<DepartmentResponse>>>();
        Result<PagedResult<DepartmentResponse>> listResult = await list.HandleAsync(new GetDepartmentsQuery(1, 50, name));
        Assert.Contains(listResult.Value!.Items, d => d.Id == departmentId);
    }

    [Fact]
    public async Task Create_department_with_duplicate_name_is_rejected()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        string name = $"Dept-{Guid.NewGuid():N}";

        ICommandHandler<CreateDepartmentCommand, Guid> create = sp.GetRequiredService<ICommandHandler<CreateDepartmentCommand, Guid>>();
        Result<Guid> first = await create.HandleAsync(new CreateDepartmentCommand(name));
        Assert.True(first.IsSuccess);

        Result<Guid> second = await create.HandleAsync(new CreateDepartmentCommand(name));
        Assert.True(second.IsFailure);
    }

    [Fact]
    public async Task Delete_department_deactivates_it()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        ICommandHandler<CreateDepartmentCommand, Guid> create = sp.GetRequiredService<ICommandHandler<CreateDepartmentCommand, Guid>>();
        Guid departmentId = (await create.HandleAsync(new CreateDepartmentCommand($"Dept-{Guid.NewGuid():N}"))).Value;

        ICommandHandler<DeleteDepartmentCommand> delete = sp.GetRequiredService<ICommandHandler<DeleteDepartmentCommand>>();
        Result deleteResult = await delete.HandleAsync(new DeleteDepartmentCommand(departmentId));
        Assert.True(deleteResult.IsSuccess);

        IQueryHandler<GetDepartmentQuery, DepartmentResponse> get = sp.GetRequiredService<IQueryHandler<GetDepartmentQuery, DepartmentResponse>>();
        Result<DepartmentResponse> afterDelete = await get.HandleAsync(new GetDepartmentQuery(departmentId));
        Assert.False(afterDelete.Value!.IsActive);
    }

    [Fact]
    public async Task AssignDepartmentHead_makes_the_employee_a_member_then_unassign_clears_it()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        Guid employeeId = await CreateEmployeeAsync(sp);

        ICommandHandler<CreateDepartmentCommand, Guid> createDept = sp.GetRequiredService<ICommandHandler<CreateDepartmentCommand, Guid>>();
        Guid departmentId = (await createDept.HandleAsync(new CreateDepartmentCommand($"Dept-{Guid.NewGuid():N}"))).Value;

        ICommandHandler<AssignDepartmentHeadCommand> assignHead = sp.GetRequiredService<ICommandHandler<AssignDepartmentHeadCommand>>();
        Result assignResult = await assignHead.HandleAsync(new AssignDepartmentHeadCommand(departmentId, employeeId));
        Assert.True(assignResult.IsSuccess);

        IQueryHandler<GetDepartmentQuery, DepartmentResponse> get = sp.GetRequiredService<IQueryHandler<GetDepartmentQuery, DepartmentResponse>>();
        Result<DepartmentResponse> afterAssign = await get.HandleAsync(new GetDepartmentQuery(departmentId));
        Assert.Equal(employeeId, afterAssign.Value!.DepartmentHeadId);

        // Assigning a second head while one is already assigned must be rejected.
        Guid otherEmployeeId = await CreateEmployeeAsync(sp);
        Result secondAssign = await assignHead.HandleAsync(new AssignDepartmentHeadCommand(departmentId, otherEmployeeId));
        Assert.True(secondAssign.IsFailure);

        ICommandHandler<UnassignDepartmentHeadCommand> unassign = sp.GetRequiredService<ICommandHandler<UnassignDepartmentHeadCommand>>();
        Result unassignResult = await unassign.HandleAsync(new UnassignDepartmentHeadCommand(departmentId));
        Assert.True(unassignResult.IsSuccess);

        Result<DepartmentResponse> afterUnassign = await get.HandleAsync(new GetDepartmentQuery(departmentId));
        Assert.Null(afterUnassign.Value!.DepartmentHeadId);
    }

    [Fact]
    public async Task MoveEmployee_between_departments_then_remove_leaves_no_active_membership()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        Guid employeeId = await CreateEmployeeAsync(sp);

        ICommandHandler<CreateDepartmentCommand, Guid> createDept = sp.GetRequiredService<ICommandHandler<CreateDepartmentCommand, Guid>>();
        Guid departmentAId = (await createDept.HandleAsync(new CreateDepartmentCommand($"Dept-A-{Guid.NewGuid():N}"))).Value;
        Guid departmentBId = (await createDept.HandleAsync(new CreateDepartmentCommand($"Dept-B-{Guid.NewGuid():N}"))).Value;

        ICommandHandler<AssignEmployeeToDepartmentCommand> assign = sp.GetRequiredService<ICommandHandler<Hr.Contracts.Employees.AssignEmployeeToDepartmentCommand>>();
        Result assignResult = await assign.HandleAsync(
            new Hr.Contracts.Employees.AssignEmployeeToDepartmentCommand(employeeId, departmentAId));
        Assert.True(assignResult.IsSuccess);

        ICommandHandler<MoveEmployeeToDepartmentCommand> move = sp.GetRequiredService<ICommandHandler<MoveEmployeeToDepartmentCommand>>();
        Result moveResult = await move.HandleAsync(new MoveEmployeeToDepartmentCommand(employeeId, departmentBId));
        Assert.True(moveResult.IsSuccess);

        ICommandHandler<RemoveEmployeeFromDepartmentCommand> remove = sp.GetRequiredService<ICommandHandler<RemoveEmployeeFromDepartmentCommand>>();
        Result removeResult = await remove.HandleAsync(new RemoveEmployeeFromDepartmentCommand(employeeId, departmentBId));
        Assert.True(removeResult.IsSuccess);

        // HR Phase 2 (K1): removing needs an ACTIVE membership. An already-inactive row (like
        // departmentA's, left behind by the move above) used to be a no-op success; it is now
        // UserNotInDepartment, the same as no row at all.
        Result removeInactive = await remove.HandleAsync(new RemoveEmployeeFromDepartmentCommand(employeeId, departmentAId));
        Assert.True(removeInactive.IsFailure);
        Assert.Equal("departments.UserNotInDepartment", removeInactive.Error.Code);

        Guid otherDepartmentId = (await sp.GetRequiredService<ICommandHandler<CreateDepartmentCommand, Guid>>()
            .HandleAsync(new CreateDepartmentCommand($"Dept-C-{Guid.NewGuid():N}"))).Value;
        Result removeNeverAssigned = await remove.HandleAsync(new RemoveEmployeeFromDepartmentCommand(employeeId, otherDepartmentId));
        Assert.True(removeNeverAssigned.IsFailure);
    }

    private static async Task<Guid> CreateEmployeeAsync(IServiceProvider sp)
    {
        (Guid employeeId, _) = await HrTestSeeding.HireEmployeeAsync(
            sp, $"dept-user-{Guid.NewGuid():N}@example.com");

        return employeeId;
    }
}
