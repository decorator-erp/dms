using BuildingBlocks.Messaging;
using FluentValidation;
using Identity.Contracts.Roles;
using Identity.Domain;
using Identity.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using SharedKernel;

namespace Identity.Application.Features.Roles.UpdateRole;

internal sealed class UpdateRoleCommandValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleCommandValidator()
    {
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Description).MaximumLength(500).When(x => x.Description is not null);
    }
}

/// <summary>Goes straight to <see cref="RoleManager{TRole}"/>: updating the name and the description
/// needs both <see cref="ApplicationRole.Rename"/> and <see cref="ApplicationRole.UpdateDescription"/>
/// before the single <c>UpdateAsync</c> call. (<c>IRoleService</c> had a rename-only update that
/// nothing called; Phase 5 deleted it.)</summary>
internal sealed class UpdateRoleCommandHandler(RoleManager<ApplicationRole> roleManager)
    : ICommandHandler<UpdateRoleCommand>
{
    public async Task<Result> HandleAsync(UpdateRoleCommand command, CancellationToken ct = default)
    {
        ApplicationRole? role = await roleManager.FindByIdAsync(command.RoleId);
        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        // N4: a system role keeps its exact name (seeders and provisioning find it by name); its
        // description is still editable.
        if (RoleService.IsSystemRole(role.Name) && !string.Equals(role.Name, command.Name, StringComparison.Ordinal))
        {
            return RoleErrors.SystemRole(role.Name!);
        }

        role.Rename(command.Name);
        role.UpdateDescription(command.Description);

        IdentityResult result = await roleManager.UpdateAsync(role);
        return result.Succeeded
            ? Result.Success()
            : Error.BadRequest("roles.UpdateFailed", string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
