using BuildingBlocks.Messaging;
using FluentValidation;
using Identity.Contracts.Roles;
using Identity.Domain;
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

/// <summary>N1: only a full-access caller may take a full-access role away from someone.</summary>
internal sealed class RemoveRoleFromUserCommandHandler(IRoleService roleService, IDelegationGuard delegationGuard)
    : ICommandHandler<RemoveRoleFromUserCommand>
{
    public async Task<Result> HandleAsync(RemoveRoleFromUserCommand command, CancellationToken ct = default)
    {
        ApplicationRole? role = await roleService.GetByIdAsync(command.RoleId, ct);
        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        Result manageable = await delegationGuard.EnsureCanManageRoleAsync(role, ct);
        return manageable.IsFailure
            ? manageable
            : await roleService.RemoveRoleFromUserAsync(command.UserId, command.RoleId, ct);
    }
}
