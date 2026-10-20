using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts.Departments;
using Hr.Domain;
using Hr.Domain.Errors;
using Hr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Hr.Infrastructure.Services;
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

/// <summary>K2: removing an active member. Not a member (or already removed) is
/// <c>departments.UserNotInDepartment</c> (400), where removing twice used to "succeed"; the department's
/// head is <c>departments.RemoveHeadFirst</c> (409). See <see cref="IDepartmentMembershipService.RemoveAsync"/>.</summary>
internal sealed class RemoveEmployeeFromDepartmentCommandHandler(IDepartmentMembershipService memberships)
    : ICommandHandler<RemoveEmployeeFromDepartmentCommand>
{
    public Task<Result> HandleAsync(RemoveEmployeeFromDepartmentCommand command, CancellationToken ct = default) =>
        memberships.RemoveAsync(command.EmployeeId, command.DepartmentId, ct);
}
