using SharedKernel;

namespace Identity.Domain;

internal static class AuthErrors
{
    public static readonly Error UnAuthorization =
        Error.Unauthorized("authentications.UnAuthorization", "You are not authorized.");

    public static readonly Error InvalidCredentials =
        Error.Unauthorized("authentications.InvalidCredentials", "Invalid email or password.");

    public static readonly Error UserNotActive =
        Error.Unauthorized("authentications.UserNotActive", "User account is not active.");

    public static readonly Error InvalidToken =
        Error.Unauthorized("authentications.InvalidToken", "Invalid token.");

    public static readonly Error InvalidRefreshToken =
        Error.Unauthorized("authentications.InvalidRefreshToken", "Invalid refresh token.");

    public static readonly Error InvalidUser =
        Error.Unauthorized("authentications.InvalidUser", "Invalid user.");

    public static readonly Error InvalidCode =
        Error.Unauthorized("authentications.InvalidCode", "Invalid code.");

    public static readonly Error SendResetPasswordError =
        Error.BadRequest("authentications.SendResetPasswordError", "Could not send the reset password code.");

    public static readonly Error ResetPasswordError =
        Error.BadRequest("authentications.ResetPasswordError", "Could not reset the password.");
}
