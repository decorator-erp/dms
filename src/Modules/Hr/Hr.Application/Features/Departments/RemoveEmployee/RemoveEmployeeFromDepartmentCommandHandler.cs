using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts.Departments;
using Hr.Domain;
using Hr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Hr.Application.Features.Departments.RemoveEmployee;

internal sealed class RemoveEmployeeFromDepartmentCommandValidator : AbstractValidator<RemoveEmployeeFromDepartmentCommand>
{
    public RemoveEmployeeFromDepartmentCommandValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
        RuleFor(x => x.DepartmentId).NotEmpty();
    }
}

internal sealed class RemoveEmployeeFromDepartmentCommandHandler(HrDbContext db)
    : ICommandHandler<RemoveEmployeeFromDepartmentCommand>
{
    public async Task<Result> HandleAsync(RemoveEmployeeFromDepartmentCommand command, CancellationToken ct = default)
    {
        var membership = await db.EmployeeDepartments.FirstOrDefaultAsync(
            ed => ed.EmployeeId == command.EmployeeId && ed.DepartmentId == command.DepartmentId, ct);

        if (membership is null)
            return DepartmentErrors.UserNotInDepartment;

        membership.Remove();
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
