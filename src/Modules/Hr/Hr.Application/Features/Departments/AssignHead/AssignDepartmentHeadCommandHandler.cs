using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts.Departments;
using Hr.Domain;
using Hr.Domain.Errors;
using Hr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Hr.Infrastructure.Services;
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
/// already, rather than requiring a separate call first.
///
/// <para>Phase 2: the head must be an active employee (<c>employees.Inactive</c>, 409) who is not a member
/// of another department (K1, <c>employees.AlreadyInAnotherDepartment</c>), and a former membership is
/// restored rather than duplicated. See <see cref="IDepartmentMembershipService.AssignHeadAsync"/>.</para></summary>
internal sealed class AssignDepartmentHeadCommandHandler(IDepartmentMembershipService memberships)
    : ICommandHandler<AssignDepartmentHeadCommand>
{
    public Task<Result> HandleAsync(AssignDepartmentHeadCommand command, CancellationToken ct = default) =>
        memberships.AssignHeadAsync(command.DepartmentId, command.EmployeeId, ct);
}
