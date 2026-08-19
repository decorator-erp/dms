using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks;
using Finance.Contracts;
using Identity.Contracts;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// The ninth module's boot surface: registered, permissioned, and routed.
///
/// <para>Everything here goes through the real host over HTTP, which is what makes it a smoke test
/// rather than another handler test — the question is whether <c>Program.cs</c>'s module list, the
/// permission registry and the endpoint scan all agree that Finance exists.</para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class FinanceSmokeTests(ApiFixture fixture)
{
    private HttpClient AuthorizedClient(params string[] permissions)
    {
        var client = fixture.CreateClient();
        var token = fixture.IssueTestToken(Guid.NewGuid().ToString(), permissions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// All twelve, not a sample. <c>FinancePermissions.All</c> is the one list <c>FinanceModule</c>
    /// registers and this counts, so the number cannot drift from the constants.
    /// </summary>
    [Fact]
    public async Task GetPermissions_ContainsAllTwelveFinancePermissionStrings()
    {
        var client = AuthorizedClient(IdentityPermissions.PermissionsManage);

        var modules = await client.GetFromJsonAsync<List<ModulePermissionSet>>("/api/permissions");
        Assert.NotNull(modules);

        var finance = Assert.Single(modules!, m => m.Module == "Finance");

        Assert.Equal(FinancePermissions.All.Count, finance.Permissions.Count);
        foreach (var permission in FinancePermissions.All)
            Assert.Contains(permission, finance.Permissions);

        // The three that carry a SECOND dot -- the ones an inventory grep of `finance\.[a-z_]+`
        // truncates to "finance.variations" and collapses into one. That miss is why the plan's count
        // was eight and the real one is twelve.
        Assert.Contains("finance.variations.approve", finance.Permissions);
        Assert.Contains("finance.variations.sign", finance.Permissions);
        Assert.Contains("finance.variations.apply", finance.Permissions);
    }

    /// <summary>
    /// The routes really are mounted. Asserted against <c>/openapi/v1.json</c> — the document Scalar
    /// itself reads — rather than by calling each one, so a route that exists but 500s still counts as
    /// mounted, which is the property being checked here.
    /// </summary>
    [Fact]
    public async Task OpenApiDocument_ListsTheFinanceRoutes()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");

        var mounted = paths.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        // Liveness: an empty or mis-shaped document would make every Contains below vacuous.
        Assert.NotEmpty(mounted);

        // One per Finance-owned noun group.
        Assert.Contains("/api/cost-categories", mounted);
        Assert.Contains("/api/budget-lines", mounted);
        Assert.Contains("/api/boq-snapshots", mounted);
        Assert.Contains("/api/ledger-entries", mounted);
        Assert.Contains("/api/invoices", mounted);
        Assert.Contains("/api/variations", mounted);
    }

    /// <summary>
    /// <b>The route-shape-versus-module-ownership divergence, asserted rather than only documented.</b>
    ///
    /// <para>These sixteen routes live under Projects' and their children's URL space and are served
    /// entirely by Finance. They are the first place in the system where the URL tree and the module
    /// boundary deliberately disagree, and this is the test that would fail if someone "tidied" them
    /// into Projects — which would make Projects read <c>finance.*</c> tables and break rule 3.</para>
    /// </summary>
    [Fact]
    public async Task OpenApiDocument_ListsTheFinanceRoutesMountedUnderProjectsUrlSpace()
    {
        var client = fixture.CreateClient();

        using var document = JsonDocument.Parse(
            await client.GetStringAsync("/openapi/v1.json"));

        var mounted = document.RootElement.GetProperty("paths")
            .EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(mounted);

        Assert.Contains("/api/projects/{id}/financial/summary", mounted);
        Assert.Contains("/api/projects/{id}/financial/movements", mounted);
        Assert.Contains("/api/projects/{id}/financial/by-category", mounted);
        Assert.Contains("/api/projects/{id}/financial/cashflow", mounted);
        Assert.Contains("/api/projects/{projectId}/financial-dashboard", mounted);
        Assert.Contains("/api/projects/{id}/invoices", mounted);
        Assert.Contains("/api/projects/{id}/variations", mounted);
        Assert.Contains("/api/projects/{id}/boq-snapshots", mounted);

        Assert.Contains("/api/project-stages/{id}/financial/movements", mounted);
        Assert.Contains("/api/project-stages/{id}/invoices", mounted);
        Assert.Contains("/api/project-stages/{id}/variations", mounted);
        Assert.Contains("/api/project-stages/{id}/boq-snapshots", mounted);

        Assert.Contains("/api/project-tasks/{id}/financial/movements", mounted);
        Assert.Contains("/api/project-tasks/{id}/invoice-lines", mounted);
        Assert.Contains("/api/project-tasks/{id}/boq-lines", mounted);
        Assert.Contains("/api/project-tasks/{id}/variations", mounted);

        // And Projects' OWN routes still resolve on the same prefix -- two modules mapping groups under
        // one prefix is ordinary routing, and this is the assertion that proves no collision occurred.
        Assert.Contains("/api/projects", mounted);
    }

    /// <summary>
    /// Authorization order, at the route layer: no token is 401, a token without
    /// <c>finance.read</c> is 403. The route-claim check runs before any handler, so neither answer
    /// depends on the id in the URL existing.
    /// </summary>
    [Fact]
    public async Task FinanceRoutes_Require_Authentication_ThenPermission()
    {
        var anonymous = fixture.CreateClient();
        var unauthorized = await anonymous.GetAsync("/api/cost-categories");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var wrongPermission = AuthorizedClient(IdentityPermissions.PermissionsManage);
        var forbidden = await wrongPermission.GetAsync("/api/cost-categories");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var allowed = AuthorizedClient(FinancePermissions.Read);
        var ok = await allowed.GetAsync("/api/cost-categories");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    /// <summary>
    /// The separately-grantable variation transitions really are separate: <c>finance.manage_variation</c>
    /// does not open the approve route. This is the permission split's whole purpose, and a single
    /// re-used constant would make it decorative.
    /// </summary>
    [Fact]
    public async Task ApprovingAVariation_RequiresItsOwnPermission_NotTheAuthoringOne()
    {
        var authoringOnly = AuthorizedClient(FinancePermissions.ManageVariation);

        var response = await authoringOnly.PutAsJsonAsync(
            $"/api/variations/{Guid.NewGuid()}/approve", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
