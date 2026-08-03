using Identity.Domain;

namespace Identity.Infrastructure.Authentication;

internal interface IJwtProvider
{
    Task<(string Token, int ExpireMinutes)> GenerateTokenAsync(
        ApplicationUser user, IEnumerable<string> roles, CancellationToken ct = default);

    string? ValidateToken(string token);
}
