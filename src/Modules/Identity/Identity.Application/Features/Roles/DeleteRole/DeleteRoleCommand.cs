using BuildingBlocks.Messaging;
using FluentValidation;
using Identity.Infrastructure.Services;
using SharedKernel;

namespace Identity.Application.Features.Roles.DeleteRole;

internal sealed record DeleteRoleCommand(string RoleId) : ICommand;

internal sealed class DeleteRoleCommandValidator : AbstractValidator<DeleteRoleCommand>
{
    public DeleteRoleCommandValidator() => RuleFor(x => x.RoleId).NotEmpty();
}

internal sealed class DeleteRoleCommandHandler(IRoleService roleService) : ICommandHandler<DeleteRoleCommand>
{
    public Task<Result> HandleAsync(DeleteRoleCommand command, CancellationToken ct = default) =>
        roleService.DeleteRoleAsync(command.RoleId, ct);
}
