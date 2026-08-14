using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Crm.Contracts;
using Identity.Contracts;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// G9-14's own verify block: the app boots with all SIX modules; <c>GET /api/permissions</c> contains
/// all 56 <c>CrmPermissions.All</c> strings; unauthenticated → 401 on a protected CRM route; and the
/// phase's first anonymous surface — <c>POST /api/crm/public/leads</c> — is reachable with no bearer
/// token at all (never a 401/403), while every other CRM route still gates normally. Same shape
/// <c>ProjectsSmokeTests</c>/<c>DocumentsSmokeTests</c> established, one module wider.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CrmSmokeTests(ApiFixture fixture)
{
    [Fact]
    public async Task GetPermissions_ContainsAllCrmModulePermissionStrings()
    {
        var client = fixture.CreateClient();
        var token = fixture.IssueTestToken(Guid.NewGuid().ToString(), IdentityPermissions.PermissionsManage);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/permissions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var modules = await response.Content.ReadFromJsonAsync<List<ModulePermissionSet>>();
        Assert.NotNull(modules);

        var crmModule = Assert.Single(modules!, m => m.Module == "Crm");
        // The full 56, not a sample - CrmPermissions.All is the one list CrmModule registers and this
        // is the assertion that number can never silently drift from the constants again.
        Assert.Equal(CrmPermissions.All.Count, crmModule.Permissions.Count);
        foreach (var permission in CrmPermissions.All)
            Assert.Contains(permission, crmModule.Permissions);
    }

    [Fact]
    public async Task GetLeads_Unauthenticated_Returns401()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/api/crm/leads");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// E2-03: merges what were two separate tests (reachability, burst) into one sequential six-request
    /// drive against the SAME fixed-window limit (5/minute/IP, per <c>Api.DI.AddApi</c>) - split across
    /// two tests, both shared that one server-side IP partition with no <c>ApiFixture.ResetAsync</c>
    /// between them (never called - see that method's own doc comment), so whichever test's six requests
    /// ran first exhausted the window for the other, and the loser saw <c>TooManyRequests</c> instead of
    /// its own expected status. One test removes the ordering dependency while keeping both proofs:
    ///
    /// <para>The anonymous surface's own reachability proof - no bearer token, yet the FIRST response is
    /// NEVER 401/403 (the shape every other CRM route returns for the same missing-auth case) - the
    /// request reaches the handler itself, which then succeeds (202) or fails closed on its own terms
    /// (503 if <c>Crm:PublicLeadIntakeOwnerId</c> were ever unset - see <c>ApiFixture.PublicLeadIntakeOwnerId</c>,
    /// which this fixture always configures, so 202 is the actual expected outcome here).</para>
    ///
    /// <para>A burst past the limit trips 429 on the SIXTH request - proving the limiter is actually
    /// wired, not just declared. All requests share one <see cref="HttpClient"/> (and therefore one
    /// client-side connection), but the limiter partitions by <c>HttpContext.Connection.RemoteIpAddress</c>
    /// server-side, which is what actually matters here.</para>
    /// </summary>
    [Fact]
    public async Task SubmitPublicLead_FirstOfSixIsReachable_SixthTripsTheRateLimit()
    {
        var client = fixture.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");

        var first = await client.PostAsJsonAsync("/api/crm/public/leads", new
        {
            FirstName = "Anon",
            LastName = $"Prospect-{suffix}",
            Email = $"anon-{suffix}@example.com",
        });
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        HttpResponseMessage? last = null;
        for (var i = 0; i < 5; i++)
        {
            last = await client.PostAsJsonAsync("/api/crm/public/leads", new
            {
                FirstName = "Burst",
                LastName = $"Prospect-{i}-{Guid.NewGuid():N}",
            });
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
    }
}
