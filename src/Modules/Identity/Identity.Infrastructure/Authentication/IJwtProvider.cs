using Identity.Domain;

namespace Identity.Infrastructure.Authentication;

internal interface IJwtProvider
{
    Task<(string Token, int ExpireMinutes)> GenerateTokenAsync(
        ApplicationUser user, IEnumerable<string> roles, CancellationToken ct = default);

    /// <summary>
    /// Returns the token's <c>sub</c> claim if the signature, issuer, and audience are valid, or
    /// <c>null</c> otherwise. <paramref name="validateLifetime"/> defaults to <c>true</c> for normal
    /// bearer validation; the refresh flow passes <c>false</c> deliberately — refreshing an expired
    /// access token is the entire point of the flow, so it must accept an expired-but-validly-signed
    /// token rather than reject it outright (a real bug in the backend this ports from: its
    /// <c>ValidateToken</c> always validated lifetime, so an expired access token could never be
    /// refreshed).
    /// </summary>
    string? ValidateToken(string token, bool validateLifetime = true);
}
