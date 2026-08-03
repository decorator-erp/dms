using BuildingBlocks.Messaging;
using FluentValidation;
using Identity.Contracts.Roles;
using Identity.Infrastructure.Services;
using SharedKernel;

namespace Identity.Application.Features.Roles.CreateRole;

internal sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
}

internal sealed class CreateRoleCommandHandler(IRoleService roleService) : ICommandHandler<CreateRoleCommand, string>
{
    public Task<Result<string>> HandleAsync(CreateRoleCommand command, CancellationToken ct = default) =>
        roleService.CreateRoleAsync(command.Name, ct);
}
