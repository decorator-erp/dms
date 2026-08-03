using BuildingBlocks.Messaging;
using FluentValidation;
using Identity.Contracts.Roles;
using Identity.Domain;
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

/// <summary>Goes straight to <see cref="RoleManager{TRole}"/> rather than through
/// <c>IRoleService.UpdateRoleAsync</c> — that method only renames (it predates this task); updating the
/// description too needs both <see cref="ApplicationRole.Rename"/> and
/// <see cref="ApplicationRole.UpdateDescription"/> before the single <c>UpdateAsync</c> call.</summary>
internal sealed class UpdateRoleCommandHandler(RoleManager<ApplicationRole> roleManager)
    : ICommandHandler<UpdateRoleCommand>
{
    public async Task<Result> HandleAsync(UpdateRoleCommand command, CancellationToken ct = default)
    {
        var role = await roleManager.FindByIdAsync(command.RoleId);
        if (role is null)
            return RoleErrors.NotFound;

        role.Rename(command.Name);
        role.UpdateDescription(command.Description);

        var result = await roleManager.UpdateAsync(role);
        return result.Succeeded
            ? Result.Success()
            : Error.BadRequest("roles.UpdateFailed", string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
