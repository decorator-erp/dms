using BuildingBlocks.Messaging;
using FluentValidation;
using Identity.Contracts;
using Identity.Contracts.Permissions;
using Identity.Infrastructure.Services;
using Identity.Domain;
using SharedKernel;

namespace Identity.Application.Features.Permissions.AssignPermissionToRole;

internal sealed class AssignPermissionToRoleCommandValidator : AbstractValidator<AssignPermissionToRoleCommand>
{
    public AssignPermissionToRoleCommandValidator(IPermissionCatalog catalog)
    {
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.Permission)
            .NotEmpty()
            .Must(p => catalog.All().SelectMany(m => m.Permissions).Contains(p))
            .WithMessage(x => $"'{x.Permission}' is not a recognized permission.");
    }
}

/// <summary>N1: adding a permission to a role confers it on every member, so the caller must hold it;
/// a full-access role's claims are only for a full-access caller to touch (and then N3 refuses the edit
/// itself, 409, in <see cref="IPermissionService.AssignPermissionToRoleAsync"/>).</summary>
internal sealed class AssignPermissionToRoleCommandHandler(
    IPermissionService permissionService, IRoleService roleService, IDelegationGuard delegationGuard)
    : ICommandHandler<AssignPermissionToRoleCommand>
{
    public async Task<Result> HandleAsync(AssignPermissionToRoleCommand command, CancellationToken ct = default)
    {
        ApplicationRole? role = await roleService.GetByIdAsync(command.RoleId, ct);
        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        Result manageable = await delegationGuard.EnsureCanManageRoleAsync(role, ct);
        if (manageable.IsFailure)
        {
            return manageable;
        }

        Result delegable = await delegationGuard.EnsureCallerHoldsAsync([command.Permission], ct);
        return delegable.IsFailure
            ? delegable
            : await permissionService.AssignPermissionToRoleAsync(command.RoleId, command.Permission, ct);
    }
}
