using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts.Departments;
using Hr.Domain;
using Hr.Domain.Errors;
using Hr.Infrastructure.Persistence;
using Hr.Infrastructure.Services;
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
/// (<c>u =&gt; u.EmployeeId == employeeId</c>), rewriting the employee's whole membership history.
///
/// <para>K1 makes the fix structural: an employee is active in at most one department, so "the
/// department they're moving from" is simply their one active membership, and there is no source id
/// to pass. (This comment used to say the command required one; it never did.) The move ends that
/// membership and starts the new one, restoring the pair's row if they were in that department before.
/// A department head is not moved out from under their department: unassign the head first. The rules
/// live in <see cref="IDepartmentMembershipService.MoveAsync"/>.</para>
/// </summary>
internal sealed class MoveEmployeeToDepartmentCommandHandler(IDepartmentMembershipService memberships)
    : ICommandHandler<MoveEmployeeToDepartmentCommand>
{
    public Task<Result> HandleAsync(MoveEmployeeToDepartmentCommand command, CancellationToken ct = default) =>
        memberships.MoveAsync(command.EmployeeId, command.NewDepartmentId, ct);
}
