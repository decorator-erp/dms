using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using BuildingBlocks;
using Identity.Contracts;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// Pins <see cref="TestUsers"/>, the HR/Identity Phase 0 harness that Phases 1A and 1B build their
/// authorization and session tests on. If a token the harness hands out is not a real login token, or
/// carries more than the test asked for, every 403 those phases assert proves nothing. These tests make
/// that a red test instead of a silent one.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class HttpHarnessTests(ApiFixture fixture)
{
    [Fact]
    public async Task RealToken_CarriesExactlyTheRolesPermissions()
    {
        TestSession session = await TestUsers.CreateUserWithPermissionsAsync(
            fixture, IdentityPermissions.RolesRead, IdentityPermissions.UsersRead);

        JwtSecurityToken token = new JwtSecurityTokenHandler().ReadJwtToken(session.AccessToken);

        Assert.Equal(session.UserId, token.Subject);
        Assert.Equal(
            [IdentityPermissions.RolesRead, IdentityPermissions.UsersRead],
            token.Claims.Where(c => c.Type == PermissionClaim.Type).Select(c => c.Value).Order());
    }

    [Fact]
    public async Task RealToken_IsAuthorizedForItsPermission_AndForbiddenWithoutOne()
    {
        TestSession session = await TestUsers.CreateUserWithPermissionsAsync(fixture, IdentityPermissions.RolesRead);

        HttpResponseMessage read = await session.Client.GetAsync("/api/roles");
        HttpResponseMessage create = await session.Client.PostAsJsonAsync(
            "/api/roles", new { Name = $"harness-denied-{Guid.NewGuid():N}" });

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task UserWithNoPermissions_IsAuthenticated_ButForbiddenFromAGatedRoute()
    {
        TestSession session = await TestUsers.CreateUserWithPermissionsAsync(fixture);

        JwtSecurityToken token = new JwtSecurityTokenHandler().ReadJwtToken(session.AccessToken);
        HttpResponseMessage response = await session.Client.GetAsync("/api/roles");

        Assert.DoesNotContain(token.Claims, c => c.Type == PermissionClaim.Type);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NoToken_IsUnauthorized()
    {
        HttpResponseMessage response = await fixture.CreateClient().GetAsync("/api/roles");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
