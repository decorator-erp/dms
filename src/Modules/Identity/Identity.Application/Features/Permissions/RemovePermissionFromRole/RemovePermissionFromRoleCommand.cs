using BuildingBlocks.Messaging;
using FluentValidation;
using Identity.Contracts.Permissions;
using Identity.Infrastructure.Services;
using Identity.Domain;
using SharedKernel;

namespace Identity.Application.Features.Permissions.RemovePermissionFromRole;

internal sealed class RemovePermissionFromRoleCommandValidator : AbstractValidator<RemovePermissionFromRoleCommand>
{
    public RemovePermissionFromRoleCommandValidator()
    {
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.Permission).NotEmpty();
    }
}

/// <summary>N1: claim-editing a full-access role is for a full-access caller only (who then meets N3's
/// 409 in <see cref="IPermissionService.RemovePermissionFromRoleAsync"/>). Removing a permission confers
/// nothing, so there is no hold-what-you-remove check.</summary>
internal sealed class RemovePermissionFromRoleCommandHandler(
    IPermissionService permissionService, IRoleService roleService, IDelegationGuard delegationGuard)
    : ICommandHandler<RemovePermissionFromRoleCommand>
{
    public async Task<Result> HandleAsync(RemovePermissionFromRoleCommand command, CancellationToken ct = default)
    {
        ApplicationRole? role = await roleService.GetByIdAsync(command.RoleId, ct);
        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        Result manageable = await delegationGuard.EnsureCanManageRoleAsync(role, ct);
        return manageable.IsFailure
            ? manageable
            : await permissionService.RemovePermissionFromRoleAsync(command.RoleId, command.Permission, ct);
    }
}
