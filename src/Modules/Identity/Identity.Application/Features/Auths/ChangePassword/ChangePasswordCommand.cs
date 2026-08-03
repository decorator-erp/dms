using BuildingBlocks.Messaging;
using FluentValidation;
using Identity.Contracts.Auth;
using Identity.Domain;
using Microsoft.AspNetCore.Identity;
using SharedKernel;

namespace Identity.Application.Features.Auths.ChangePassword;

internal sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.Request.UserId).NotEmpty();
        RuleFor(x => x.Request.CurrentPassword).NotEmpty();
        RuleFor(x => x.Request.NewPassword).NotEmpty().MinimumLength(8);
    }
}

/// <summary>Not in the backend — the closest there is <c>ResetPassword</c> (token-based, for a
/// forgotten password). This is the ordinary "I know my current password and want a new one" path,
/// written fresh over <see cref="UserManager{TUser}.ChangePasswordAsync"/>.</summary>
internal sealed class ChangePasswordCommandHandler(UserManager<ApplicationUser> userManager)
    : ICommandHandler<ChangePasswordCommand>
{
    public async Task<Result> HandleAsync(ChangePasswordCommand command, CancellationToken ct = default)
    {
        var request = command.Request;

        var user = await userManager.FindByIdAsync(request.UserId);
        if (user is null)
            return UserErrors.NotFound;

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);

        return result.Succeeded
            ? Result.Success()
            : Error.BadRequest("authentications.ChangePasswordFailed", string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
