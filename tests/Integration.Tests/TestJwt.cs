using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BuildingBlocks;
using Microsoft.IdentityModel.Tokens;

namespace Integration.Tests;

/// <summary>Issues a test JWT with a given permission set, signed with the same key/issuer/audience
/// the test host validates against — see <see cref="ApiFixture"/>.</summary>
public static class TestJwt
{
    /// <param name="securityStamp">The user's current security stamp, sent as <c>sstamp</c> (N2); the
    /// host refuses a token whose stamp is not the stored one.</param>
    public static string Issue(
        string signingKey, string issuer, string audience, string userId, string securityStamp,
        params string[] permissions)
    {
        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, userId),
            new(ClaimTypes.NameIdentifier, userId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("sstamp", securityStamp),
            .. permissions.Select(p => new Claim(PermissionClaim.Type, p)),
        ];

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
