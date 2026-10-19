namespace Identity.Application.Features.Auths;

/// <summary>New refresh-token values: 64 random bytes, base64. Only their SHA-256 hash is ever stored
/// (K5, <c>RefreshToken.Hash</c>); the value itself goes to the caller once and nowhere else.</summary>
internal static class RefreshTokenValues
{
    public static string New() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
}
