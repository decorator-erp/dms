using BuildingBlocks;
using BuildingBlocks.Messaging;
using FluentValidation;
using Identity.Contracts.Auth;
using Identity.Domain;
using Identity.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using SharedKernel;

namespace Identity.Application.Features.Auths.ChangePassword;

internal sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.Request.CurrentPassword).NotEmpty();
        RuleFor(x => x.Request.NewPassword).NotEmpty().MinimumLength(8);
    }
}

/// <summary>Not in the backend — the closest there is <c>ResetPassword</c> (token-based, for a
/// forgotten password). This is the ordinary "I know my current password and want a new one" path,
/// written fresh over <see cref="UserManager{TUser}.ChangePasswordAsync"/>.
///
/// <para>K8: the account is always the caller's own, from <see cref="ICurrentUser"/>. The request used to
/// carry a <c>UserId</c>, so any signed-in user could change any other user's password, given only the
/// current password.</para>
/// <para>K5/N2: a changed password ends every other session. All refresh tokens are revoked, and
/// <c>ChangePasswordAsync</c> bumps the security stamp, so live access tokens are refused with
/// <c>StaleToken</c>. The client signs in again with the new password.</para></summary>
internal sealed class ChangePasswordCommandHandler(
    UserManager<ApplicationUser> userManager, ICurrentUser currentUser, IUserSessionCache sessionCache)
    : ICommandHandler<ChangePasswordCommand>
{
    public async Task<Result> HandleAsync(ChangePasswordCommand command, CancellationToken ct = default)
    {
        ChangePasswordRequest request = command.Request;

        if (currentUser.UserId is null)
        {
            return AuthErrors.UnAuthorization;
        }

        ApplicationUser? user = await userManager.FindByIdAsync(currentUser.UserId);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        // Revoked first, so the UpdateAsync inside ChangePasswordAsync (the one that writes the new hash
        // and stamp) saves the revocations in the same write.
        user.RevokeAllRefreshTokens(DateTime.UtcNow);
        IdentityResult result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return Error.BadRequest("authentications.ChangePasswordFailed", string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        sessionCache.Evict(user.Id);
        return Result.Success();
    }
}
