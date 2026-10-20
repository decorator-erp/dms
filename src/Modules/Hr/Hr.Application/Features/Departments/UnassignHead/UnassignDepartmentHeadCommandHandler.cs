using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts.Departments;
using Hr.Domain;
using Hr.Domain.Errors;
using Hr.Infrastructure.Persistence;
using Hr.Infrastructure.Services;
using SharedKernel;

namespace Hr.Application.Features.Departments.UnassignHead;

internal sealed class UnassignDepartmentHeadCommandValidator : AbstractValidator<UnassignDepartmentHeadCommand>
{
    public UnassignDepartmentHeadCommandValidator() => RuleFor(x => x.DepartmentId).NotEmpty();
}

internal sealed class UnassignDepartmentHeadCommandHandler(IDepartmentMembershipService memberships)
    : ICommandHandler<UnassignDepartmentHeadCommand>
{
    public Task<Result> HandleAsync(UnassignDepartmentHeadCommand command, CancellationToken ct = default) =>
        memberships.UnassignHeadAsync(command.DepartmentId, ct);
}
