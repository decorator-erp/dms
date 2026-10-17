using BuildingBlocks.Messaging;
using FluentValidation;
using Identity.Contracts.Permissions;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Identity.Application.Features.Permissions.RevokePermissionOverride;

internal sealed class RevokePermissionOverrideCommandValidator : AbstractValidator<RevokePermissionOverrideCommand>
{
    public RevokePermissionOverrideCommandValidator()
    {
        RuleFor(x => x.TargetUserId).NotEmpty();
        RuleFor(x => x.Permission).NotEmpty();
    }
}

/// <summary>Removes the override row entirely (a user reverts to whatever their roles alone confer) —
/// not the same as flipping it to a grant/deny; there is nothing left to flip.</summary>
internal sealed class RevokePermissionOverrideCommandHandler(IdentityModuleDbContext db)
    : ICommandHandler<RevokePermissionOverrideCommand>
{
    public async Task<Result> HandleAsync(RevokePermissionOverrideCommand command, CancellationToken ct = default)
    {
        int rowsDeleted = await db.UserPermissionOverrides
            .Where(o => o.UserId == command.TargetUserId && o.Permission == command.Permission)
            .ExecuteDeleteAsync(ct);

        return rowsDeleted == 0 ? PermissionErrors.PermissionOverrideNotFound : Result.Success();
    }
}
