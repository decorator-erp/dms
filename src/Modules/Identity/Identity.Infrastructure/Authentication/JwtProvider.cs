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
/// the backend), and one <see cref="PermissionClaim.Type"/> claim per resolved permission.</summary>
internal sealed class JwtProvider(IOptions<JwtOptions> options, IEffectivePermissionService effectivePermissionService)
    : IJwtProvider
{
    private readonly JwtOptions _options = options.Value;

    public async Task<(string Token, int ExpireMinutes)> GenerateTokenAsync(
        ApplicationUser user, IEnumerable<string> roles, CancellationToken ct = default)
    {
        var permissions = await effectivePermissionService.ResolveAsync(user.Id, ct);

        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("name", user.UserName ?? string.Empty),
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
        };

        try
        {
            var principal = handler.ValidateToken(token, validationParameters, out _);
            return principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
    }
}
