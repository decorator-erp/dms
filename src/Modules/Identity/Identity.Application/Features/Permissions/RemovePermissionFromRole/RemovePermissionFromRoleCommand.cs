using BuildingBlocks.Messaging;
using FluentValidation;
using Identity.Infrastructure.Services;
using SharedKernel;

namespace Identity.Application.Features.Permissions.RemovePermissionFromRole;

internal sealed record RemovePermissionFromRoleCommand(string RoleId, string Permission) : ICommand;

internal sealed class RemovePermissionFromRoleCommandValidator : AbstractValidator<RemovePermissionFromRoleCommand>
{
    public RemovePermissionFromRoleCommandValidator()
    {
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.Permission).NotEmpty();
    }
}

internal sealed class RemovePermissionFromRoleCommandHandler(IPermissionService permissionService)
    : ICommandHandler<RemovePermissionFromRoleCommand>
{
    public Task<Result> HandleAsync(RemovePermissionFromRoleCommand command, CancellationToken ct = default) =>
        permissionService.RemovePermissionFromRoleAsync(command.RoleId, command.Permission, ct);
}
