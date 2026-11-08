using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Crm.Contracts;
using Finance.Contracts;
using Identity.Contracts;
using Notifications.Contracts;
using Projects.Contracts;
using Sites.Contracts;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// S1-08's own verify block: <c>GET /api/permissions</c> contains all 7 <c>SitesPermissions.All</c>
/// strings; the system total is what it actually is rather than what a plan predicted; the OpenAPI
/// document lists the site routes; and an unauthenticated call to a protected site route is a 401.
///
/// <para>The seven-module boot assertion lives in <c>CrmSmokeTests</c> and was updated at S1-04, not
/// here — a module registers when <c>Program.cs</c> loads its assembly, three tasks before it has an
/// endpoint to serve.</para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SitesSmokeTests(ApiFixture fixture)
{
    /// <summary>
    /// The system-wide permission count.
    ///
    /// <para><b>114 through Phase 5, not the 62 that plan predicted.</b> <c>PLAN-PHASE5.md</c> arrived
    /// at 62 by adding Sites' permissions to CRM's own 56 — the number that appears in
    /// <c>CrmSmokeTests</c> — rather than to the system total. The real arithmetic: Approvals 3 + Crm 56
    /// + Documents 6 + Hr 17 + Identity 4 + Projects 21 = 107, plus Sites' 7.</para>
    ///
    /// <para><b>N1-04 adds Notifications' 4, and the total is now RECOUNTED rather than re-typed</b>
    /// (D2's rule: recount, don't trust the arithmetic). The seven-module figure stays a literal — it is
    /// the historical fact the paragraph above explains — and the eighth module's contribution is added
    /// from <c>NotificationsPermissions.All.Count</c>, so it cannot drift from the constant that
    /// <c>NotificationsModule</c> actually registers. The same trick the per-module assertions below
    /// already use.</para>
    ///
    /// <para><b>F1-09 adds Finance's 12 the same way</b>, and the pattern is now the rule rather than a
    /// one-off: every module past the seventh contributes <c>&lt;Module&gt;Permissions.All.Count</c>, so
    /// the only literal in this expression is the frozen historical figure. Re-typing the new total
    /// would make this test agree with itself and with nothing else — which is exactly how
    /// <c>PLAN-PHASE5.md</c> arrived at 62 when the answer was 114.</para>
    ///
    /// <para><b>The scope of work (SOW-4) adds Projects' 3 <c>scope.*</c> codes the same way</b>, from
    /// <c>ProjectsPermissions.Scope.All</c> — the list <c>ProjectsModule</c> registers.</para>
    ///
    /// <para><b>The CRM backend completion (PR C) adds CRM codes the same way</b>, named from their
    /// constants in <see cref="CrmAddedByBackendCompletion"/>, so the frozen 114 stays the historical
    /// figure.</para>
    /// </summary>
    private const int PermissionsThroughPhase5 = 114;

    /// <summary>C-1 <c>crm.clients.read_all</c> and C-8 <c>crm.notes.manage_all</c>.</summary>
    private static readonly string[] CrmAddedByBackendCompletion = [CrmPermissions.Clients.ReadAll, CrmPermissions.Notes.ManageAll];

    /// <summary>crm-complete E1 (D1): <c>project.manage_members</c>.</summary>
    private static readonly string[] ProjectsAddedByCrmComplete = [ProjectsPermissions.Projects.ManageMembers];

    /// <summary>P1-A: <c>project.manage_access</c> (project access grants).</summary>
    private static readonly string[] ProjectsAddedByAccessGrants = [ProjectsPermissions.Projects.ManageAccess];

    private static int TotalPermissionsAcrossAllModules =>
        PermissionsThroughPhase5 + NotificationsPermissions.All.Count + FinancePermissions.All.Count
        + ProjectsPermissions.Scope.All.Count + CrmAddedByBackendCompletion.Length + ProjectsAddedByCrmComplete.Length
        + ProjectsAddedByAccessGrants.Length;

    [Fact]
    public async Task GetPermissions_ContainsAllSevenSitesPermissionStrings()
    {
        HttpClient client = fixture.CreateClient();
        string token = fixture.IssueTestToken(Guid.NewGuid().ToString(), IdentityPermissions.PermissionsManage);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.GetAsync("/api/permissions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        List<ModulePermissionSet>? modules = await response.Content.ReadFromJsonAsync<List<ModulePermissionSet>>();
        Assert.NotNull(modules);

        ModulePermissionSet sitesModule = Assert.Single(modules!, m => m.Module == "Sites");

        // The full 7, not a sample -- SitesPermissions.All is the one list SitesModule registers and
        // this counts, so the number cannot drift from the constants.
        Assert.Equal(SitesPermissions.All.Count, sitesModule.Permissions.Count);
        foreach (string permission in SitesPermissions.All)
        {
            Assert.Contains(permission, sitesModule.Permissions);
        }

        // The seventh string is the one PLAN-PHASE5.md's grep could not see, because its pattern
        // excluded the dot. It gates 18 of the 20 definition routes.
        Assert.Contains("sites.definitions.manage", sitesModule.Permissions);
    }

    [Fact]
    public async Task GetPermissions_TotalsAcrossAllNineModules()
    {
        HttpClient client = fixture.CreateClient();
        string token = fixture.IssueTestToken(Guid.NewGuid().ToString(), IdentityPermissions.PermissionsManage);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        List<ModulePermissionSet>? modules = await client.GetFromJsonAsync<List<ModulePermissionSet>>("/api/permissions");

        Assert.NotNull(modules);
        Assert.Equal(TotalPermissionsAcrossAllModules, modules!.Sum(m => m.Permissions.Count));
    }

    /// <summary>
    /// The real form of "<c>/scalar</c> lists site routes". Scalar renders an HTML page with no route
    /// list to grep; the document it reads is the OpenAPI one at <c>/openapi/v1.json</c>, which is what
    /// this asserts — the same content Scalar's UI would present. Same technique M1-02 introduced after
    /// <c>PLAN-PHASE4.md:311</c>'s equivalent requirement went unwritten for a whole phase.
    /// </summary>
    [Fact]
    public async Task OpenApiDocument_ListsTheSiteRoutes()
    {
        HttpClient client = fixture.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string document = await response.Content.ReadAsStringAsync();

        // The survey surface.
        Assert.Contains("/api/sites", document);
        Assert.Contains("/api/sites/{siteId}/submit", document);
        Assert.Contains("/api/sites/{siteId}/approve", document);
        Assert.Contains("/api/sites/{siteId}/records", document);
        Assert.Contains("/api/sites/{siteId}/records/bulk", document);
        Assert.Contains("/api/sites/{siteId}/families", document);
        Assert.Contains("/api/sites/{siteId}/boq", document);
        Assert.Contains("/api/sites/{siteId}/documents", document);

        // The definition surface.
        Assert.Contains("/api/site-modules", document);
        Assert.Contains("/api/site-modules/{code}/schema", document);
        Assert.Contains("/api/site-items", document);
        Assert.Contains("/api/site-items/{itemId}/usage", document);

        // Deliberately absent: the variance route is deferred [D8] because nothing writes
        // Site.SourceBriefId, so it could only ever answer "no source brief".
        Assert.DoesNotContain("/api/sites/{siteId}/variance", document);
    }

    [Fact]
    public async Task AProtectedSiteRoute_WithoutAToken_Is401()
    {
        HttpClient client = fixture.CreateClient();

        HttpResponseMessage response = await client.GetAsync($"/api/sites?projectId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Authenticated but without <c>sites.read</c> is a 403, not a 401 — the route-claim layer of
    /// <c>CLAUDE.md</c>'s authorization order, before the scope decorator's 404 and before any aggregate
    /// state check.
    /// </summary>
    [Fact]
    public async Task AProtectedSiteRoute_WithATokenLackingThePermission_Is403()
    {
        HttpClient client = fixture.CreateClient();
        string token = fixture.IssueTestToken(Guid.NewGuid().ToString(), IdentityPermissions.PermissionsManage);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.GetAsync($"/api/sites?projectId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// The definition layer's read/write split, which is why <c>sites.definitions.manage</c> exists:
    /// holding only <c>sites.read</c> renders the form but cannot edit it.
    /// </summary>
    [Fact]
    public async Task ADefinitionWrite_WithOnlySitesRead_Is403_WhileTheReadSucceeds()
    {
        HttpClient client = fixture.CreateClient();
        string token = fixture.IssueTestToken(Guid.NewGuid().ToString(), SitesPermissions.Read);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage read = await client.GetAsync("/api/site-modules");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        HttpResponseMessage write = await client.PostAsJsonAsync("/api/site-modules", new
        {
            Code = $"m{Guid.NewGuid():N}"[..12],
            NameEn = "Openings",
            NameAr = "الفتحات",
        });

        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }
}
