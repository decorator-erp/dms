using BuildingBlocks.Messaging;
using FluentValidation;
using Identity.Contracts;
using Identity.Infrastructure.Services;
using SharedKernel;

namespace Identity.Application.Features.Permissions.AssignPermissionToRole;

internal sealed record AssignPermissionToRoleCommand(string RoleId, string Permission) : ICommand;

/// <summary>Validates <see cref="AssignPermissionToRoleCommand.Permission"/> against
/// <see cref="IPermissionCatalog"/>'s union of every module's registered strings — never a hard-coded
/// permission list, per the plan's explicit instruction for this task.</summary>
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

internal sealed class AssignPermissionToRoleCommandHandler(IPermissionService permissionService)
    : ICommandHandler<AssignPermissionToRoleCommand>
{
    public Task<Result> HandleAsync(AssignPermissionToRoleCommand command, CancellationToken ct = default) =>
        permissionService.AssignPermissionToRoleAsync(command.RoleId, command.Permission, ct);
}
