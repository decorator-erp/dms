using System.IdentityModel.Tokens.Jwt;
using BuildingBlocks;
using Identity.Domain;
using Identity.Infrastructure.Authentication;
using Identity.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Identity.Infrastructure.Tests;

/// <summary>G1-04 verify: a token issued for a user carries exactly the resolved permission
/// claims.</summary>
[Collection(IdentityInfrastructureCollection.Name)]
public sealed class JwtProviderTests(IdentityInfrastructureFixture fixture)
{
    [Fact]
    public async Task GenerateTokenAsync_TokenCarriesExactlyTheResolvedPermissions()
    {
        await using var scope = fixture.CreateScope();
        var (user, role) = await TestDataBuilder.CreateUserInRoleAsync(
            scope.ServiceProvider, "employees.read", "departments.read", "roles.manage");

        // An override on top of the role grants, so the test also proves the token reflects
        // EffectivePermissionService's resolution (roles ∪ overrides), not just the raw role claims.
        var db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();
        db.UserPermissionOverrides.Add(UserPermissionOverride.Deny(user.Id, "roles.manage", "admin-1"));
        await db.SaveChangesAsync();

        var jwtProvider = scope.ServiceProvider.GetRequiredService<IJwtProvider>();
        var (token, _) = await jwtProvider.GenerateTokenAsync(user, [role.Name!]);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var permissionClaims = jwt.Claims
            .Where(c => c.Type == PermissionClaim.Type)
            .Select(c => c.Value)
            .ToHashSet();

        Assert.Equal(new HashSet<string> { "employees.read", "departments.read" }, permissionClaims);
        Assert.DoesNotContain("roles.manage", permissionClaims);
    }

    [Fact]
    public async Task GenerateTokenAsync_ThenValidateToken_ReturnsTheSameUserId()
    {
        await using var scope = fixture.CreateScope();
        var (user, role) = await TestDataBuilder.CreateUserInRoleAsync(scope.ServiceProvider, "employees.read");

        var jwtProvider = scope.ServiceProvider.GetRequiredService<IJwtProvider>();
        var (token, _) = await jwtProvider.GenerateTokenAsync(user, [role.Name!]);

        var validatedUserId = jwtProvider.ValidateToken(token);

        Assert.Equal(user.Id, validatedUserId);
    }
}
