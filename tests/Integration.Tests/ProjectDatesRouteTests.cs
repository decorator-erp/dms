using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// H-dates over HTTP. The project form sends <c>YYYY-MM-DD</c>; that binds as a <c>DateTime</c> of kind
/// Unspecified, which Npgsql refuses to write to a <c>timestamptz</c> column (it was a 500). Create and update
/// now store a date-only value as that day at midnight UTC, and a value with an offset as its UTC instant.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ProjectDatesRouteTests(ApiFixture fixture)
{
    [Fact]
    public async Task Create_and_update_accept_date_only_and_offset_estimated_dates()
    {
        string suffix = Guid.NewGuid().ToString("N");
        (_, string ownerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"dates-owner-{suffix}@example.com");
        HttpClient owner = fixture.CreateClient();
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.IssueTestToken(
            ownerUserId, "project.create", "project.read", "project.update", "clients.create", "clients.read"));

        HttpResponseMessage clientResponse = await owner.PostAsJsonAsync("/api/clients", ClientRequests.NewClientBody("dates"));
        Assert.Equal(HttpStatusCode.Created, clientResponse.StatusCode);
        Guid clientId = (await clientResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("clientId").GetGuid();

        string body = $$"""
            {"code":"DT-{{suffix}}","name":"Dates","address":"1 Date Rd","workflowType":2,"clientId":"{{clientId}}",
             "estimatedStartDate":"2026-12-01","estimatedEndDate":"2027-03-31"}
            """;
        HttpResponseMessage created = await owner.PostAsync("/api/projects", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Guid projectId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("projectId").GetGuid();

        JsonElement project = await owner.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}");
        Assert.Equal(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), project.GetProperty("estimatedStartDate").GetDateTime().ToUniversalTime());
        Assert.Equal(new DateTime(2027, 3, 31, 0, 0, 0, DateTimeKind.Utc), project.GetProperty("estimatedEndDate").GetDateTime().ToUniversalTime());

        const string update = """{"estimatedStartDate":"2027-01-15","estimatedEndDate":"2027-04-30T10:00:00+02:00"}""";
        HttpResponseMessage updated = await owner.PutAsync($"/api/projects/{projectId}", new StringContent(update, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);

        project = await owner.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}");
        Assert.Equal(new DateTime(2027, 1, 15, 0, 0, 0, DateTimeKind.Utc), project.GetProperty("estimatedStartDate").GetDateTime().ToUniversalTime());
        Assert.Equal(new DateTime(2027, 4, 30, 8, 0, 0, DateTimeKind.Utc), project.GetProperty("estimatedEndDate").GetDateTime().ToUniversalTime());
    }
}
