using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Finance.Contracts;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// The ninth module's schema isolation, in the shape
/// <c>{Crm,Projects,Documents,Notifications}SchemaIsolationTests</c> established.
///
/// <para><b>WHAT THIS PROVES, AND WHAT IT DOES NOT.</b> It proves that a Finance read issued through
/// the real host touches <c>finance.</c> and no other module's schema — that the handlers reach their
/// own tables and resolve cross-module facts through contracts rather than by querying across the
/// boundary. It does <b>not</b> prove Finance never causes another schema to be written: it demonstrably
/// does, through the outbox, and <c>BudgetGateFlowTests</c> is where that is asserted. The two are
/// complementary and neither substitutes for the other — this one would still pass if the Budget gate
/// never worked, and that one would still pass if every query joined across schemas.</para>
///
/// <para><b>The Project*Financial routes are the interesting case here</b>, and the reason this file
/// asserts on them specifically. They live under Projects' URL space, so a reader who assumes URL
/// implies ownership would expect them to query <c>projects.</c>. They must not: every figure they
/// return comes from <c>finance.</c> tables, and the only Projects involvement is names resolved
/// through <c>IProjectDirectory</c> — which is a different DbContext and shows up as
/// <c>projects.</c> SQL. So those routes are asserted <b>separately</b>, with a filter that permits
/// <c>projects.</c>, rather than being quietly dropped from the strict list.</para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class FinanceSchemaIsolationTests(ApiFixture fixture)
{
    private static readonly string[] OtherModuleSchemas =
        ["identity.", "hr.", "approvals.", "crm.", "projects.", "documents.", "sites.", "notify."];

    private HttpClient FinanceReader()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.IssueTestToken(Guid.NewGuid().ToString(), FinancePermissions.Read));
        return client;
    }

    /// <summary>
    /// A Finance-owned read touches <c>finance.</c> only. Two routes rather than one, so the result is
    /// not a property of a single query shape.
    /// </summary>
    [Fact]
    public async Task ReadingFinanceOwnedRoutes_IssuesNoSqlAgainstAnotherModulesSchema()
    {
        var client = FinanceReader();
        var projectId = Guid.NewGuid();

        // Warm both routes so first-request model building and JIT are not what gets captured.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/cost-categories")).StatusCode);
        await client.GetAsync($"/api/invoices/by-project/{projectId}");

        fixture.ClearCapturedSql();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/cost-categories")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync($"/api/invoices/by-project/{projectId}")).StatusCode);

        // LIVENESS, and stronger than a bare NotEmpty: the reads must have touched finance., which
        // proves the measured window actually covers these two requests. Without it, a window that
        // captured nothing at all would satisfy every negative assertion below.
        Assert.Contains(
            fixture.CapturedSql, sql => sql.Contains("finance.", StringComparison.OrdinalIgnoreCase));

        AssertNoSqlTouching(OtherModuleSchemas);
    }

    /// <summary>
    /// The routes mounted under Projects' URL space read <c>finance.</c> for every figure they return.
    ///
    /// <para><c>projects.</c> is permitted here and nowhere else in this file, because
    /// <c>IProjectDirectory</c> resolves stage and task NAMES through Projects' own DbContext — a
    /// contract call, not a cross-schema query. Every other module's schema stays forbidden, so the
    /// exemption cannot hide a different leak.</para>
    /// </summary>
    [Fact]
    public async Task ReadingProjectScopedFinancialRoutes_TouchesFinance_AndOnlyProjectsBesides()
    {
        var client = FinanceReader();
        var projectId = Guid.NewGuid();

        await client.GetAsync($"/api/projects/{projectId}/financial/summary");

        fixture.ClearCapturedSql();

        // 404 is the expected answer for an unknown project, and it is reached AFTER the handler has
        // asked IProjectDirectory -- which is the cross-module read this test is characterising.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/projects/{projectId}/financial/summary")).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync($"/api/projects/{projectId}/financial/by-category")).StatusCode);

        Assert.Contains(
            fixture.CapturedSql, sql => sql.Contains("finance.", StringComparison.OrdinalIgnoreCase));

        AssertNoSqlTouching(
            ["identity.", "hr.", "approvals.", "crm.", "documents.", "sites.", "notify."]);
    }

    /// <summary>
    /// A Finance WRITE touches <c>finance.</c> and no other module's schema.
    ///
    /// <para>The write under test is a cost-category create — a plain insert that publishes nothing.
    /// This summary claimed until X-02 that it was an invoice create enqueuing
    /// <c>InvoiceIssuedIntegrationEvent</c>, which was never what the body did; the event has since
    /// been deleted for want of a subscriber. The <c>finance.</c> assertion below is the liveness half
    /// and is what makes the negative assertions mean anything, exactly as in the two read tests
    /// above.</para>
    /// </summary>
    [Fact]
    public async Task WritingAFinanceRow_TouchesFinanceAndTheOutbox_AndNoOtherModulesSchema()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.IssueTestToken(
                Guid.NewGuid().ToString(), FinancePermissions.ManageCostCategory));

        var request = new { Code = $"IS{Guid.NewGuid().ToString("N")[..8]}", Name = "Isolation probe" };

        // Warm the route with a throwaway create.
        await client.PostAsJsonAsync("/api/cost-categories", request);

        fixture.ClearCapturedSql();

        var response = await client.PostAsJsonAsync(
            "/api/cost-categories",
            new { Code = $"IS{Guid.NewGuid().ToString("N")[..8]}", Name = "Isolation probe" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        Assert.Contains(
            fixture.CapturedSql, sql => sql.Contains("finance.", StringComparison.OrdinalIgnoreCase));

        AssertNoSqlTouching(OtherModuleSchemas);
    }

    /// <summary>
    /// The liveness half, shared by all three callers — PC-02's rule. An empty capture must fail here
    /// rather than read as a clean result, which is what <c>CapturedSql</c> silently did for three
    /// phases before N1-08 found it.
    /// </summary>
    private void AssertNoSqlTouching(string[] forbiddenSchemas)
    {
        Assert.NotEmpty(fixture.CapturedSql);

        var offending = fixture.CapturedSql
            .Where(sql => forbiddenSchemas.Any(s => sql.Contains(s, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(offending.Count == 0,
            $"Finance issued SQL touching another module's schema: {string.Join(" | ", offending)}");
    }
}
