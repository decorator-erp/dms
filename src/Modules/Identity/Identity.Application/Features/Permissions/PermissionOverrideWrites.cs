using BuildingBlocks;
using Identity.Contracts;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Persistence.Configurations;
using Identity.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SharedKernel;

namespace Identity.Application.Features.Permissions;

/// <summary>
/// K6: the one body behind grant and deny. The two handlers used to be copies of each other that
/// differed only in <c>UserPermissionOverride.Grant</c> vs <c>.Deny</c>, so every rule added to one had
/// to be remembered in the other. Checks run in CLAUDE.md's order: who is asking (401), whether they may
/// (403), whether the target exists (404), then the state of the data (409). A successful write bumps the
/// target's security stamp (N2), so their tokens refresh into the new set.
/// <list type="number">
/// <item>Nobody writes overrides on their own account (<c>permissions.SelfModification</c>, 403).</item>
/// <item>A grant confers a permission, so the caller must effectively hold it (N1,
/// <c>permissions.CannotDelegate</c>, 403).</item>
/// <item>One live override per (user, permission) is a data invariant (409, as before). An expired one is
/// renewed in place with the new direction, reason, expiry and grantor (Aug 2.4), and goes through the
/// same security-stamp bump as any other write.</item>
/// <item>Denying <c>permissions.manage</c> must leave at least one other active holder, or nobody
/// could ever repair permissions again (<c>permissions.LastHolder</c>, 409).</item>
/// </list>
/// A static helper rather than a registered service: Application has no DI hook of its own besides the
/// handler scan, and the handlers already hold every dependency it needs.
/// </summary>
internal static class PermissionOverrideWrites
{
    public static async Task<Result> WriteAsync(
        IdentityModuleDbContext db,
        ICurrentUser currentUser,
        IDelegationGuard delegationGuard,
        IEffectivePermissionService effectivePermissions,
        UserManager<ApplicationUser> userManager,
        ISecurityStamps securityStamps,
        OverrideWrite write,
        CancellationToken ct)
    {
        if (currentUser.UserId is null)
        {
            return AuthErrors.UnAuthorization;
        }

        if (write.TargetUserId == currentUser.UserId)
        {
            return PermissionErrors.SelfModification;
        }

        if (write.IsGrant)
        {
            Result delegable = await delegationGuard.EnsureCallerHoldsAsync([write.Permission], ct);
            if (delegable.IsFailure)
            {
                return delegable;
            }
        }

        if (await userManager.FindByIdAsync(write.TargetUserId) is null)
        {
            return UserErrors.NotFound;
        }

        // Aug 2.4: only a LIVE override blocks a new one. An expired one is renewed in place below (the
        // unique index on (UserId, Permission) covers expired rows too, so it can't be inserted beside).
        UserPermissionOverride? existing = await db.UserPermissionOverrides.FirstOrDefaultAsync(
            o => o.UserId == write.TargetUserId && o.Permission == write.Permission, ct);
        if (existing is { IsExpired: false })
        {
            return PermissionErrors.PermissionOverrideAlreadyExists(write.Permission);
        }

        if (!write.IsGrant
            && write.Permission == IdentityPermissions.PermissionsManage
            && !await effectivePermissions.HasOtherActiveHolderAsync(write.Permission, write.TargetUserId, ct))
        {
            return PermissionErrors.LastHolder(write.Permission);
        }

        if (existing is not null)
        {
            existing.Renew(write.IsGrant, currentUser.UserId, write.Reason, write.ExpiresAtUtc);
        }
        else
        {
            db.UserPermissionOverrides.Add(write.IsGrant
                ? UserPermissionOverride.Grant(
                    write.TargetUserId, write.Permission, currentUser.UserId, write.Reason, write.ExpiresAtUtc)
                : UserPermissionOverride.Deny(
                    write.TargetUserId, write.Permission, currentUser.UserId, write.Reason, write.ExpiresAtUtc));
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: UserPermissionOverrideConfiguration.UniqueUserPermissionIndex,
        })
        {
            // Aug 3.5: a concurrent write inserted the row between the check above and this save. It is
            // the same refusal the check gives. Return at once: the failed statement aborted the command's
            // transaction, so nothing else may run on it (no bump).
            return PermissionErrors.PermissionOverrideAlreadyExists(write.Permission);
        }

        // N2: the target's tokens carry their old permission set.
        await securityStamps.BumpAsync(write.TargetUserId, ct);
        return Result.Success();
    }
}

internal sealed record OverrideWrite(
    string TargetUserId, string Permission, bool IsGrant, string? Reason, DateTime? ExpiresAtUtc);
