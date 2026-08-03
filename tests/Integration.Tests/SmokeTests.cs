using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace Integration.Tests;

/// <summary>G0-09 verify: app boots against a container, migrations apply, a protected route returns
/// 401 unauthenticated. Also proves the test-JWT helper actually works (a token it issues is accepted),
/// not just that it compiles.</summary>
[Collection(ApiCollection.Name)]
public sealed class SmokeTests(ApiFixture fixture)
{
    [Fact]
    public async Task Health_Returns200()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedRoute_WithoutToken_Returns401()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedRoute_WithValidTestToken_Returns200()
    {
        var client = fixture.CreateClient();
        var token = fixture.IssueTestToken("11111111-1111-1111-1111-111111111111");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
