using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts.Departments;
using Hr.Domain;
using Hr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Hr.Application.Features.Departments.AssignHead;

internal sealed class AssignDepartmentHeadCommandValidator : AbstractValidator<AssignDepartmentHeadCommand>
{
    public AssignDepartmentHeadCommandValidator()
    {
        RuleFor(x => x.DepartmentId).NotEmpty();
        RuleFor(x => x.EmployeeId).NotEmpty();
    }
}

/// <summary>Ported from <c>DepartmentDomainService.AssignDepartmentHeadAsync</c>: assigning a head also
/// ensures that employee is a member of the department, adding the membership row if it isn't there
/// already, rather than requiring a separate call first.</summary>
internal sealed class AssignDepartmentHeadCommandHandler(HrDbContext db) : ICommandHandler<AssignDepartmentHeadCommand>
{
    public async Task<Result> HandleAsync(AssignDepartmentHeadCommand command, CancellationToken ct = default)
    {
        var department = await db.Departments.FindAsync([command.DepartmentId], ct);
        if (department is null)
            return DepartmentErrors.NotFound;

        if (department.DepartmentHeadId.HasValue)
            return DepartmentErrors.AlreadyHaveHead;

        if (!await db.Employees.AnyAsync(e => e.Id == command.EmployeeId, ct))
            return EmployeeErrors.NotFound;

        var isMember = await db.EmployeeDepartments.AnyAsync(
            ed => ed.EmployeeId == command.EmployeeId && ed.DepartmentId == command.DepartmentId, ct);
        if (!isMember)
            db.EmployeeDepartments.Add(EmployeeDepartment.Create(command.EmployeeId, command.DepartmentId));

        department.AssignHead(command.EmployeeId);
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
