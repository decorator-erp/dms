using BuildingBlocks;
using BuildingBlocks.Messaging;
using FluentValidation;
using Identity.Contracts.Permissions;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Services;
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
/// not the same as flipping it to a grant/deny; there is nothing left to flip.
/// <para>K6's self-modification rule covers revoke as well as grant and deny: revoking a deny is how a
/// user would lift one placed on themselves.</para></summary>
internal sealed class RevokePermissionOverrideCommandHandler(
    IdentityModuleDbContext db, ICurrentUser currentUser, ISecurityStamps securityStamps)
    : ICommandHandler<RevokePermissionOverrideCommand>
{
    public async Task<Result> HandleAsync(RevokePermissionOverrideCommand command, CancellationToken ct = default)
    {
        if (currentUser.UserId is null)
        {
            return AuthErrors.UnAuthorization;
        }

        if (command.TargetUserId == currentUser.UserId)
        {
            return PermissionErrors.SelfModification;
        }

        if (!await db.Users.AnyAsync(u => u.Id == command.TargetUserId, ct))
        {
            return UserErrors.NotFound;
        }

        // Aug 3.2: loaded and removed through the change tracker so the revoke is audited, which
        // ExecuteDeleteAsync was not. A missing row stays 404: revoke targets one listed override.
        UserPermissionOverride? existing = await db.UserPermissionOverrides.FirstOrDefaultAsync(
            o => o.UserId == command.TargetUserId && o.Permission == command.Permission, ct);
        if (existing is null)
        {
            return PermissionErrors.PermissionOverrideNotFound;
        }

        db.UserPermissionOverrides.Remove(existing);
        await db.SaveChangesAsync(ct);

        // N2: the target's tokens carry the override that no longer exists.
        await securityStamps.BumpAsync(command.TargetUserId, ct);
        return Result.Success();
    }
}
