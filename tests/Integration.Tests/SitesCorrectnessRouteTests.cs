using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Npgsql;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// Sites correctness fixes whose behaviour lives at the ROUTE — the actor a lifecycle command stamps is
/// resolved by the endpoint from the HTTP principal, so only a real request can show which person lands
/// in the row or the event.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SitesCorrectnessRouteTests(ApiFixture fixture)
{
    private static readonly string[] SurveyorPermissions =
    [
        "clients.create", "project.create", "project.read_all",
        "sites.read", "sites.create", "sites.update", "sites.submit",
    ];

    /// <summary>
    /// <b>S10.</b> <c>SiteSubmittedIntegrationEvent.SubmittedById</c> carried the survey's CREATOR, not
    /// the person who submitted it — the submit command had no actor at all. Resolved at the endpoint now,
    /// like <c>DecidedById</c>.
    /// </summary>
    [Fact]
    public async Task S10_SubmittedEvent_CarriesTheSubmitterNotTheCreator()
    {
        string suffix = Guid.NewGuid().ToString("N");

        (Guid creatorId, string creatorUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(
            fixture, $"s10-creator-{suffix}@example.com", jobTitle: "Surveyor");
        (Guid submitterId, string submitterUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(
            fixture, $"s10-submitter-{suffix}@example.com", jobTitle: "Surveyor");

        HttpClient creator = Client(creatorUserId, SurveyorPermissions);
        Guid siteId = await CreateSiteAsync(creator, suffix);

        HttpClient submitter = Client(submitterUserId, SurveyorPermissions);
        HttpResponseMessage submitted = await submitter.PostAsync($"/api/sites/{siteId}/submit", null);
        Assert.Equal(HttpStatusCode.NoContent, submitted.StatusCode);

        string content = await ReadOutboxContentAsync("SiteSubmittedIntegrationEvent", siteId);

        Assert.Contains(submitterId.ToString(), content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(creatorId.ToString(), content, StringComparison.OrdinalIgnoreCase);
    }

    private HttpClient Client(string userId, params string[] permissions)
    {
        HttpClient client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.IssueTestToken(userId, permissions));
        return client;
    }

    private static async Task<Guid> CreateSiteAsync(HttpClient client, string suffix)
    {
        HttpResponseMessage clientResponse = await client.PostAsJsonAsync(
            "/api/clients", ClientRequests.NewClientBody("site-fix"));
        Assert.Equal(HttpStatusCode.Created, clientResponse.StatusCode);
        Guid clientId = (await clientResponse.Content.ReadFromJsonAsync<ClientBody>())!.ClientId;

        HttpResponseMessage projectResponse = await client.PostAsJsonAsync("/api/projects", new
        {
            Code = $"F-{suffix}"[..12],
            Name = "Sites Fixes",
            Address = "1 Test Rd",
            WorkflowType = 1,
            ClientId = clientId,
        });
        Assert.Equal(HttpStatusCode.OK, projectResponse.StatusCode);
        Guid projectId = (await projectResponse.Content.ReadFromJsonAsync<ProjectBody>())!.ProjectId;

        HttpResponseMessage siteResponse = await client.PostAsJsonAsync("/api/sites", new
        {
            ProjectId = projectId,
            Data = new { Address = "12 Nasr Road" },
        });
        Assert.Equal(HttpStatusCode.Created, siteResponse.StatusCode);
        return (await siteResponse.Content.ReadFromJsonAsync<SiteBody>())!.SiteId;
    }

    private async Task<string> ReadOutboxContentAsync(string eventTypeName, Guid siteId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT content::text FROM shared.outbox_messages
            WHERE type LIKE '%' || @type || '%' AND content::text ILIKE '%' || @site || '%'
            """, connection);
        command.Parameters.AddWithValue("type", eventTypeName);
        command.Parameters.AddWithValue("site", siteId.ToString());

        return (string)(await command.ExecuteScalarAsync())!;
    }

    private sealed record ClientBody(Guid ClientId);
    private sealed record ProjectBody(Guid ProjectId);
    private sealed record SiteBody(Guid SiteId);
}
