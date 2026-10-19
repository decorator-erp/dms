using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BuildingBlocks;
using Identity.Domain;
using Identity.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Infrastructure.Authentication;

/// <summary>Ported from <c>../backend/Infrastructure/Services/Authentication/JwtProvider.cs</c>. Claim
/// shapes kept identical to the original: <c>roles</c> is a single JSON-array claim (not
/// <see cref="ClaimTypes.Role"/> — <c>[Authorize(Roles=...)]</c> won't work against this token, same as
/// the backend), and one <see cref="PermissionClaim.Type"/> claim per resolved permission.
///
/// <para>N2 adds <see cref="SecurityStampClaim"/>, the user's security stamp at issue time. The host
/// compares it with the stored stamp on every request (<c>IUserSessionState</c>), so a change to the
/// account invalidates tokens already issued.</para></summary>
internal sealed class JwtProvider(IOptions<JwtOptions> options, IEffectivePermissionService effectivePermissionService)
    : IJwtProvider
{
    /// <summary>N2: the claim carrying the user's security stamp.</summary>
    public const string SecurityStampClaim = "sstamp";

    private readonly JwtOptions _options = options.Value;

    public async Task<(string Token, int ExpireMinutes)> GenerateTokenAsync(
        ApplicationUser user, IEnumerable<string> roles, CancellationToken ct = default)
    {
        IEnumerable<string> permissions = await effectivePermissionService.ResolveAsync(user.Id, ct);

        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("name", user.UserName ?? string.Empty),
            new(SecurityStampClaim, user.SecurityStamp ?? string.Empty),
            new("roles", JsonSerializer.Serialize(roles), JsonClaimValueTypes.JsonArray),
            .. permissions.Select(p => new Claim(PermissionClaim.Type, p)),
        ];

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            expires: DateTime.UtcNow.AddMinutes(_options.ExpiryMinutes),
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), _options.ExpiryMinutes);
    }

    public string? ValidateToken(string token, bool validateLifetime = true)
    {
        // MapInboundClaims defaults to true, which silently renames the "sub" claim to
        // ClaimTypes.NameIdentifier during validation - discovered by running this, not by
        // inspection: FindFirst(JwtRegisteredClaimNames.Sub) came back null even for a token this
        // same provider had just issued.
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)),
            ValidateLifetime = validateLifetime,
            ClockSkew = TimeSpan.Zero,

            // Aug 5.1: only the algorithm this provider signs with. Without the pin, the token's own
            // header chooses how it is checked.
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        };

        try
        {
            ClaimsPrincipal principal = handler.ValidateToken(token, validationParameters, out _);
            return principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            // Aug 3.10: an oversized or structurally malformed token is thrown as ArgumentException (not
            // a SecurityTokenException) before validation starts. It is still just an invalid token: the
            // caller gets InvalidToken (401), not a 500.
            return null;
        }
    }
}
