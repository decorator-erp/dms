using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>F2 (D5): the portal profile. The calling client's identity, contacts (primary first) and active
/// projects with their active stage and progress; never notes, owner or other internal fields; another
/// client's project never appears.</summary>
[Collection(ApiCollection.Name)]
public sealed class CrmPortalProfileRouteTests(ApiFixture fixture)
{
    [Fact]
    public async Task Client_profile_has_identity_contacts_and_projects_and_no_internal_fields()
    {
        PortalWorld a = await CrmPortalTestSeeding.CreateAsync(fixture, "f2a");
        PortalWorld b = await CrmPortalTestSeeding.CreateAsync(fixture, "f2b", a.Office, a.OfficeEmployeeId, a.OfficeUserId);
        HttpClient contacts = CrmPortalTestSeeding.Client(fixture, a.OfficeUserId, "crm.contacts.create", "crm.contacts.read", "clients.read");
        HttpResponseMessage contact = await contacts.PostAsJsonAsync($"/api/crm/clients/{a.ClientId}/contacts", new
        {
            FirstName = "Mona",
            LastName = "Saleh",
            Role = 1,
            IsPrimary = true,
            Email = "mona@example.com",
            Phone = "+20100000001",
            Mobile = "+20100000002",
            Notes = "INTERNAL-NOTE-do-not-leak",
        });
        Assert.True(contact.IsSuccessStatusCode, await contact.Content.ReadAsStringAsync());

        const string route = "/api/crm/portal/profile";
        Assert.Equal(HttpStatusCode.Forbidden, (await CrmPortalTestSeeding.Client(fixture, a.ClientUserId).GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await CrmPortalTestSeeding.Client(fixture, a.OfficeUserId, "crm.portal").GetAsync(route)).StatusCode);

        HttpResponseMessage response = await CrmPortalTestSeeding.Client(fixture, a.ClientUserId, "crm.portal").GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("INTERNAL-NOTE", raw);
        Assert.DoesNotContain("\"notes\"", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ownerId", raw, StringComparison.OrdinalIgnoreCase);

        using var json = JsonDocument.Parse(raw);
        JsonElement client = json.RootElement.GetProperty("client");
        Assert.Equal(a.ClientId, client.GetProperty("id").GetGuid());
        Assert.Equal("1 Client Rd", client.GetProperty("address").GetString());
        Assert.Equal("Mona", client.GetProperty("firstName").GetString());
        Assert.Equal("Saleh", client.GetProperty("lastName").GetString());
        Assert.Equal("+20100000001", client.GetProperty("phoneNumber").GetString());
        Assert.False(string.IsNullOrWhiteSpace(client.GetProperty("email").GetString()));

        JsonElement primary = Assert.Single(json.RootElement.GetProperty("contacts").EnumerateArray());
        Assert.True(primary.GetProperty("isPrimary").GetBoolean());
        Assert.Equal("mona@example.com", primary.GetProperty("email").GetString());

        JsonElement project = Assert.Single(json.RootElement.GetProperty("projects").EnumerateArray());
        Assert.Equal(a.ProjectId, project.GetProperty("id").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(project.GetProperty("code").GetString()));
        Assert.Equal(a.StageId, project.GetProperty("activeStageId").GetGuid());
        Assert.Equal("Design", project.GetProperty("activeStageName").GetString());
        Assert.Equal(a.PeriodId, project.GetProperty("openPeriodId").GetGuid());
        Assert.Equal(0m, project.GetProperty("progressPercentage").GetDecimal());
        Assert.DoesNotContain(b.ProjectId.ToString(), raw);
    }
}
