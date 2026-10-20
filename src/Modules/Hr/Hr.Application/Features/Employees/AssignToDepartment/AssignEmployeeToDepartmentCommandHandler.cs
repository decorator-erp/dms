using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts.Employees;
using Hr.Domain;
using Hr.Domain.Errors;
using Hr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Hr.Infrastructure.Services;
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

/// <summary>K1/K2: the rules live in <see cref="IDepartmentMembershipService.AssignAsync"/>. In short: an
/// active employee, an existing department, and no active membership elsewhere (moving is
/// <c>MoveEmployee</c>'s job). Rejoining a former department restores the old row.</summary>
internal sealed class AssignEmployeeToDepartmentCommandHandler(IDepartmentMembershipService memberships)
    : ICommandHandler<AssignEmployeeToDepartmentCommand>
{
    public Task<Result> HandleAsync(AssignEmployeeToDepartmentCommand command, CancellationToken ct = default) =>
        memberships.AssignAsync(command.EmployeeId, command.DepartmentId, ct);
}
