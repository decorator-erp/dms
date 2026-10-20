using SharedKernel;

namespace Identity.Domain;

internal static class AuthErrors
{
    public static readonly Error UnAuthorization =
        Error.Unauthorized("authentications.UnAuthorization", "You are not authorized.");

    public static readonly Error InvalidCredentials =
        Error.Unauthorized("authentications.InvalidCredentials", "Invalid email or password.");

    /// <summary>K4: too many failed attempts. Says nothing about whether this attempt's password was right.</summary>
    public static readonly Error LockedOut =
        Error.Unauthorized(
            "authentications.LockedOut", "This account is temporarily locked after repeated failed sign-ins. Try again later.");

    /// <summary>K5: a refresh token that was already rotated away was presented again, so the token family
    /// is treated as stolen and every refresh token of the account has been revoked.</summary>
    public static readonly Error RefreshTokenReused =
        Error.Unauthorized("authentications.RefreshTokenReused", "Invalid refresh token.");

    public static readonly Error InvalidToken =
        Error.Unauthorized("authentications.InvalidToken", "Invalid token.");

    public static readonly Error InvalidRefreshToken =
        Error.Unauthorized("authentications.InvalidRefreshToken", "Invalid refresh token.");

    public static readonly Error InvalidUser =
        Error.Unauthorized("authentications.InvalidUser", "Invalid user.");
}
