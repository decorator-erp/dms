using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts.Employees;
using Hr.Domain;
using Hr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Hr.Application.Features.Employees.AssignToDepartment;

internal sealed class AssignEmployeeToDepartmentCommandValidator : AbstractValidator<AssignEmployeeToDepartmentCommand>
{
    public AssignEmployeeToDepartmentCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.DepartmentId).NotEmpty();
    }
}

internal sealed class AssignEmployeeToDepartmentCommandHandler(HrDbContext db)
    : ICommandHandler<AssignEmployeeToDepartmentCommand>
{
    public async Task<Result> HandleAsync(AssignEmployeeToDepartmentCommand command, CancellationToken ct = default)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == command.EmployeeId, ct))
            return EmployeeErrors.NotFound;

        if (!await db.Departments.AnyAsync(d => d.Id == command.DepartmentId, ct))
            return DepartmentErrors.NotFound;

        if (await db.EmployeeDepartments.AnyAsync(
            ed => ed.EmployeeId == command.EmployeeId && ed.DepartmentId == command.DepartmentId && ed.IsActive, ct))
            return DepartmentErrors.AlreadyInDepartment;

        db.EmployeeDepartments.Add(EmployeeDepartment.Create(command.EmployeeId, command.DepartmentId));
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
