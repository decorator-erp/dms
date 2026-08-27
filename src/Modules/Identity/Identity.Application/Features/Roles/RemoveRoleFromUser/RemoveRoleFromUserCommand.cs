using BuildingBlocks.Messaging;
using FluentValidation;
using Identity.Contracts.Roles;
using Identity.Infrastructure.Services;
using SharedKernel;

namespace Identity.Application.Features.Roles.RemoveRoleFromUser;

internal sealed class RemoveRoleFromUserCommandValidator : AbstractValidator<RemoveRoleFromUserCommand>
{
    public RemoveRoleFromUserCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.RoleId).NotEmpty();
    }
}

internal sealed class RemoveRoleFromUserCommandHandler(IRoleService roleService)
    : ICommandHandler<RemoveRoleFromUserCommand>
{
    public Task<Result> HandleAsync(RemoveRoleFromUserCommand command, CancellationToken ct = default) =>
        roleService.RemoveRoleFromUserAsync(command.UserId, command.RoleId, ct);
}
