using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Identity.Contracts.Auth;
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
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CrmSchemaIsolationTests(ApiFixture fixture)
{
    [Fact]
    public async Task GetClientById_IssuesNoSqlTouchingIdentityHrApprovalsOrProjectsSchemas()
    {
        var client = fixture.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            $"crm-schema-{suffix}@example.com", "P@ssw0rd123!", "Test", "User", null, "employee"));
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
        var appUserId = (await registerResponse.Content.ReadFromJsonAsync<RegisterResponseBody>())!.UserId;

        var token = fixture.IssueTestToken(appUserId, "clients.create", "clients.read");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // clients.AppUserId -> identity.users is a real cross-schema FK (G9-08) - a fabricated app user
        // id would fail the insert outright, so a real one (the caller's own) is needed here even
        // though this test's own assertion only cares about the GET afterward.
        var createResponse = await client.PostAsJsonAsync("/api/clients", new { AppUserId = appUserId, Address = "1 Client Rd" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var clientId = (await createResponse.Content.ReadFromJsonAsync<ClientIdBody>())!.ClientId;

        // Only the query under test is observed - registration and client creation legitimately touch
        // identity./crm. and would make the assertion meaningless if included.
        fixture.ClearCapturedSql();

        var getResponse = await client.GetAsync($"/api/clients/{clientId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

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

    private sealed record RegisterResponseBody(string UserId);
    private sealed record ClientIdBody(Guid ClientId);
}
