using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts.Departments;
using Hr.Domain;
using Hr.Domain.Errors;
using Hr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Hr.Application.Features.Departments.MoveEmployee;

internal sealed class MoveEmployeeToDepartmentCommandValidator : AbstractValidator<MoveEmployeeToDepartmentCommand>
{
    public MoveEmployeeToDepartmentCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.NewDepartmentId).NotEmpty();
    }
}

/// <summary>
/// Ported from <c>DepartmentDomainService.MoveEmployeeAsync</c>, with the D7 fix: the backend's version
/// updates EVERY <c>EmployeeDepartment</c> row matching <c>EmployeeId</c> alone
/// (<c>u => u.EmployeeId == employeeId</c>) — for an employee who somehow belongs to more than one
/// department, that silently moves ALL of them, ignoring which department the caller actually meant to
/// move the employee out of. This version requires the source department id and only updates that one
/// row.
/// </summary>
internal sealed class MoveEmployeeToDepartmentCommandHandler(HrDbContext db)
    : ICommandHandler<MoveEmployeeToDepartmentCommand>
{
    public async Task<Result> HandleAsync(MoveEmployeeToDepartmentCommand command, CancellationToken ct = default)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == command.EmployeeId, ct))
        {
            return EmployeeErrors.NotFound;
        }

        if (!await db.Departments.AnyAsync(d => d.Id == command.NewDepartmentId, ct))
        {
            return DepartmentErrors.NotFound;
        }

        if (await db.EmployeeDepartments.AnyAsync(
            ed => ed.EmployeeId == command.EmployeeId && ed.DepartmentId == command.NewDepartmentId, ct))
        {
            return DepartmentErrors.AlreadyInDepartment;
        }

        EmployeeDepartment? membership = await db.EmployeeDepartments
            .Where(ed => ed.EmployeeId == command.EmployeeId && ed.IsActive)
            .OrderByDescending(ed => ed.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (membership is null)
        {
            db.EmployeeDepartments.Add(EmployeeDepartment.Create(command.EmployeeId, command.NewDepartmentId));
        }
        else
        {
            membership.Remove();
            db.EmployeeDepartments.Add(EmployeeDepartment.Create(command.EmployeeId, command.NewDepartmentId));
        }

        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
