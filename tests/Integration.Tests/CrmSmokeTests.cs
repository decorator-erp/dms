using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Crm.Contracts;
using Identity.Contracts;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// G9-14's own verify block: the app boots with all SEVEN modules (six at G9-14; Sites joined at
/// S1-04); <c>GET /api/permissions</c> contains
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
        HttpClient client = fixture.CreateClient();
        string token = fixture.IssueTestToken(Guid.NewGuid().ToString(), IdentityPermissions.PermissionsManage);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.GetAsync("/api/permissions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        List<ModulePermissionSet>? modules = await response.Content.ReadFromJsonAsync<List<ModulePermissionSet>>();
        Assert.NotNull(modules);

        ModulePermissionSet crmModule = Assert.Single(modules!, m => m.Module == "Crm");
        // The full 56, not a sample - CrmPermissions.All is the one list CrmModule registers and this
        // is the assertion that number can never silently drift from the constants again.
        Assert.Equal(CrmPermissions.All.Count, crmModule.Permissions.Count);
        foreach (string permission in CrmPermissions.All)
        {
            Assert.Contains(permission, crmModule.Permissions);
        }
    }

    [Fact]
    public async Task GetLeads_Unauthenticated_Returns401()
    {
        HttpClient client = fixture.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/crm/leads");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// M1-02: closes the gap this class's own doc comment names — "the app boots with all N modules"
    /// was previously only implied by <see cref="GetPermissions_ContainsAllCrmModulePermissionStrings"/>
    /// succeeding, never asserted directly. <c>GET /api/permissions</c> returns one
    /// <c>ModulePermissionSet</c> per registered module — the same list <c>Program.cs</c>'s own
    /// composition root loads by assembly name — so its module count and names ARE the boot proof.
    ///
    /// <para>Updated at S1-04, not S1-08 as the Sites plan expected: this asserts REGISTRATION, and a
    /// module registers the moment <c>Program.cs</c> loads its assembly — which is this task, three
    /// tasks before Sites has an endpoint to serve. The plan put it with the endpoints because that is
    /// where the "seven modules" verify was written; the test itself does not wait that long.</para>
    ///
    /// <para><b>Eight at N1-04, and the same reasoning applied a second time.</b> PLAN-PHASE6.md put
    /// the eight-module assertion at N1-07 with the endpoints. Notifications had to be loaded three
    /// tasks earlier for two concrete reasons: <c>MapEndpoints</c> is what mounts
    /// <c>NotificationsHub</c>, so N1-04's own SignalR verify is unreachable until the host loads the
    /// assembly; and N1-05's relocated jobs resolve <c>INotificationService</c> from the host container,
    /// which does not contain it until this module registers. Registration and this assertion move
    /// together, in one commit, so the tree is never red in between.</para>
    /// </summary>
    [Fact]
    public async Task Boot_RegistersExactlyNineModules()
    {
        HttpClient client = fixture.CreateClient();
        string token = fixture.IssueTestToken(Guid.NewGuid().ToString(), IdentityPermissions.PermissionsManage);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.GetAsync("/api/permissions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        List<ModulePermissionSet>? modules = await response.Content.ReadFromJsonAsync<List<ModulePermissionSet>>();
        Assert.NotNull(modules);

        Assert.Equal(
            ["Approvals", "Crm", "Documents", "Finance", "Hr", "Identity", "Notifications", "Projects", "Sites"],
            modules!.Select(m => m.Module).OrderBy(m => m, StringComparer.Ordinal));
    }

    /// <summary>
    /// M1-02: the other gap this class's own doc comment named — <c>PLAN-PHASE4.md:311</c> required
    /// "<c>/scalar</c> lists CRM routes" as G9-14's own verify, and no test ever asserted it. Scalar
    /// itself renders an HTML page (no route list to grep); the actual route source it reads from is the
    /// OpenAPI document <c>MapOpenApi()</c> serves at <c>/openapi/v1.json</c> (mapped only in
    /// Development, which <see cref="ApiFixture"/> configures), so that document is what this asserts
    /// against — same content Scalar's UI would present, once for the Meetings endpoints file M1-02 adds.
    /// </summary>
    [Fact]
    public async Task OpenApiDocument_ListsTheNewMeetingRoutes()
    {
        HttpClient client = fixture.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string document = await response.Content.ReadAsStringAsync();
        Assert.Contains("/api/crm/meetings/propose", document);
        Assert.Contains("/api/crm/meetings/{meetingId}/reschedule", document);
        Assert.Contains("/api/crm/meetings/free-slots", document);
        Assert.Contains("/api/crm/portal/meetings", document);
        Assert.Contains("/api/crm/portal/meetings/{meetingId}/cancel", document);
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
        HttpClient client = fixture.CreateClient();
        string suffix = Guid.NewGuid().ToString("N");

        HttpResponseMessage first = await client.PostAsJsonAsync("/api/crm/public/leads", new
        {
            FirstName = "Anon",
            LastName = $"Prospect-{suffix}",
            Email = $"anon-{suffix}@example.com",
        });
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        HttpResponseMessage? last = null;
        for (int i = 0; i < 5; i++)
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
