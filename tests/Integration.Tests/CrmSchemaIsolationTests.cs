using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// G9-15's own schema-isolation assertion: a CRM-owned read issues no SQL touching
/// <c>identity.</c>/<c>hr.</c>/<c>approvals.</c>/<c>projects.</c> — same shape
/// <c>ProjectsSchemaIsolationTests</c> (G5-10) and <c>DocumentsSchemaIsolationTests</c> (G7-08)
/// established, one module wider. <c>GetClientByIdQueryHandler</c> is a single <c>CrmDbContext</c>
/// projection with no cross-module lookup at all, so — like Documents' own version — this is the
/// stronger claim: no permission-driven bypass path to reason about, just "this query never joins
/// across schemas," full stop.
///
/// <para><b>C-1 (client scope).</b> A caller without <c>crm.clients.read_all</c> is now checked by
/// <c>CrmClientScope</c>, which asks Projects for the client's projects through <c>IProjectDirectory</c> and
/// <c>IProjectAccessGuard</c> — a depth-1 Contracts call that Projects answers from its own schema. That
/// is the module boundary working, not a CRM query crossing it, so the caller here holds
/// <c>crm.clients.read_all</c> and the window measures the CRM read alone.</para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CrmSchemaIsolationTests(ApiFixture fixture)
{
    [Fact]
    public async Task GetClientById_IssuesNoSqlTouchingIdentityHrApprovalsOrProjectsSchemas()
    {
        HttpClient client = fixture.CreateClient();
        string suffix = Guid.NewGuid().ToString("N");

        string appUserId = await IdentityTestSeeding.ProvisionActiveUserAsync(
            fixture, $"crm-schema-{suffix}@example.com");

        string token = fixture.IssueTestToken(appUserId, "clients.create", "clients.read", "crm.clients.read_all");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // clients.AppUserId -> identity.users is a real cross-schema FK (G9-08) - a fabricated app user
        // id would fail the insert outright, so a real one (the caller's own) is needed here even
        // though this test's own assertion only cares about the GET afterward.
        HttpResponseMessage createResponse = await client.PostAsJsonAsync("/api/clients", ClientRequests.NewClientBody("client"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Guid clientId = (await createResponse.Content.ReadFromJsonAsync<ClientIdBody>())!.ClientId;

        // Only the query under test is observed - registration and client creation legitimately touch
        // identity./crm. and would make the assertion meaningless if included.
        fixture.ClearCapturedSql();

        HttpResponseMessage getResponse = await client.GetAsync($"/api/clients/{clientId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        // PC-02 liveness. N1-08 found CapturedSql had never captured anything, and this assertion was
        // green throughout - a dead collector and a clean system are indistinguishable from the
        // negative half alone. Asserting the CRM read itself was seen proves the instrument is live AND
        // that the measured window actually covers the operation under test.
        Assert.Contains(fixture.CapturedSql, sql => sql.Contains("crm.", StringComparison.OrdinalIgnoreCase));

        var touchedOtherSchemas = fixture.CapturedSql
            .Where(sql =>
                sql.Contains("identity.", StringComparison.OrdinalIgnoreCase) ||
                sql.Contains("hr.", StringComparison.OrdinalIgnoreCase) ||
                sql.Contains("approvals.", StringComparison.OrdinalIgnoreCase) ||
                sql.Contains("projects.", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(touchedOtherSchemas.Count == 0,
            $"GET /api/clients/{{id}} issued SQL touching identity/hr/approvals/projects: {string.Join(" | ", touchedOtherSchemas)}");
    }

    private sealed record ClientIdBody(Guid ClientId);
}
