using BuildingBlocks.Messaging;
using FluentValidation;
using Identity.Contracts.Roles;
using Identity.Infrastructure.Services;
using SharedKernel;

namespace Identity.Application.Features.Roles.AssignRoleToUser;

internal sealed class AssignRoleToUserCommandValidator : AbstractValidator<AssignRoleToUserCommand>
{
    public AssignRoleToUserCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.RoleId).NotEmpty();
    }
}

internal sealed class AssignRoleToUserCommandHandler(IRoleService roleService)
    : ICommandHandler<AssignRoleToUserCommand>
{
    public Task<Result> HandleAsync(AssignRoleToUserCommand command, CancellationToken ct = default) =>
        roleService.AssignRoleToUserAsync(command.UserId, command.RoleId, ct);
}
