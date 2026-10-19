using BuildingBlocks.Messaging;
using FluentValidation;
using Identity.Contracts.Roles;
using Identity.Infrastructure.Services;
using Identity.Domain;
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

/// <summary>N1: assigning a role confers every claim it carries, so the caller must hold them all, and
/// only a full-access caller may hand out a full-access role.</summary>
internal sealed class AssignRoleToUserCommandHandler(IRoleService roleService, IDelegationGuard delegationGuard)
    : ICommandHandler<AssignRoleToUserCommand>
{
    public async Task<Result> HandleAsync(AssignRoleToUserCommand command, CancellationToken ct = default)
    {
        ApplicationRole? role = await roleService.GetByIdAsync(command.RoleId, ct);
        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        Result delegable = await delegationGuard.EnsureCanAssignRoleAsync(role, ct);
        return delegable.IsFailure
            ? delegable
            : await roleService.AssignRoleToUserAsync(command.UserId, command.RoleId, ct);
    }
}
