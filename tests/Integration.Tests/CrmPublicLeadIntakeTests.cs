using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Crm.Contracts;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// LI-1: the public intake form saves the whole request, with no configured owner. A website lead is
/// stored unassigned (<c>ownerId: null</c>, source Website), and the project answers the client gave are
/// saved as the lead's intake in the same request. The office can create an unassigned lead too, and
/// assign it later.
///
/// <para>No intake owner is configured anywhere: <see cref="ApiFixture"/> sets no
/// <c>Crm:PublicLeadIntakeOwnerId</c>, so every submission here proves the route no longer needs one (it
/// answered 503 without it). Each test submits from its own client IP
/// (<see cref="ApiFixture.CreatePublicClient"/>), so the real rate limit never interferes; the 429 itself
/// is <see cref="CrmSmokeTests"/>.</para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CrmPublicLeadIntakeTests(ApiFixture fixture)
{
    private const string PublicLeads = "/api/crm/public/leads";

    [Fact]
    public async Task A_submission_with_project_answers_is_saved_as_an_unassigned_website_lead_with_its_intake()
    {
        string lastName = $"Intake-{Guid.NewGuid():N}";
        HttpResponseMessage response = await fixture.CreatePublicClient().PostAsJsonAsync(PublicLeads, new
        {
            FirstName = "Mona",
            LastName = lastName,
            Phone = "+201000000000",
            Notes = "Call after 5pm",
            Intake = new
            {
                JobTitle = "Doctor",
                ClientAddress = "Zamalek, Cairo",
                Sector = (int)ProjectSector.Residential,
                UnitAddress = "New Cairo",
                AreaSqm = 180.5m,
                UsedDesignerBefore = true,
                DesignerExperience = (int)PastExperience.Bad,
                DesignerExperienceReason = "Late delivery",
                Residential = new
                {
                    UnitType = (int)UnitType.Villa,
                    UnitCondition = (int)UnitCondition.SemiFinished,
                    IsLicensed = true,
                    IsRented = false,
                    FinishingPurpose = (int)FinishingPurpose.TurnkeyDelivery,
                },
            },
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        HttpClient office = OfficeClient();
        JsonElement lead = await FindLeadAsync(office, lastName);
        Assert.Equal(JsonValueKind.Null, lead.GetProperty("ownerId").ValueKind);
        Assert.Equal((int)LeadSource.Website, lead.GetProperty("source").GetInt32());
        Assert.Equal((int)LeadStatus.New, lead.GetProperty("status").GetInt32());
        Assert.Equal("+201000000000", lead.GetProperty("phone").GetString());
        Assert.Equal("Call after 5pm", lead.GetProperty("notes").GetString());

        JsonElement intake = (await office.GetFromJsonAsync<JsonElement>(
            $"/api/crm/leads/{lead.GetProperty("id").GetGuid()}/intake")).GetProperty("data");
        Assert.Equal("Doctor", intake.GetProperty("jobTitle").GetString());
        Assert.Equal("Zamalek, Cairo", intake.GetProperty("clientAddress").GetString());
        Assert.Equal((int)ProjectSector.Residential, intake.GetProperty("sector").GetInt32());
        Assert.Equal("New Cairo", intake.GetProperty("unitAddress").GetString());
        Assert.Equal(180.5m, intake.GetProperty("areaSqm").GetDecimal());
        Assert.True(intake.GetProperty("usedDesignerBefore").GetBoolean());
        Assert.Equal((int)PastExperience.Bad, intake.GetProperty("designerExperience").GetInt32());
        Assert.Equal("Late delivery", intake.GetProperty("designerExperienceReason").GetString());

        JsonElement residential = intake.GetProperty("residential");
        Assert.Equal((int)UnitType.Villa, residential.GetProperty("unitType").GetInt32());
        Assert.Equal((int)UnitCondition.SemiFinished, residential.GetProperty("unitCondition").GetInt32());
        Assert.True(residential.GetProperty("isLicensed").GetBoolean());
        Assert.False(residential.GetProperty("isRented").GetBoolean());
        Assert.Equal((int)FinishingPurpose.TurnkeyDelivery, residential.GetProperty("finishingPurpose").GetInt32());
    }

    [Fact]
    public async Task An_invalid_intake_is_a_400_and_saves_no_lead()
    {
        string lastName = $"BadIntake-{Guid.NewGuid():N}";

        HttpResponseMessage response = await fixture.CreatePublicClient().PostAsJsonAsync(PublicLeads, new
        {
            FirstName = "Omar",
            LastName = lastName,
            // An experience with no "used a designer before" - the intake's own domain rule.
            Intake = new { DesignerExperience = (int)PastExperience.Good },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountLeadsAsync(OfficeClient(), lastName));
    }

    [Fact]
    public async Task Residential_details_with_another_sector_are_a_400_and_save_no_lead()
    {
        string lastName = $"BadSector-{Guid.NewGuid():N}";

        HttpResponseMessage response = await fixture.CreatePublicClient().PostAsJsonAsync(PublicLeads, new
        {
            FirstName = "Omar",
            LastName = lastName,
            Intake = new { Sector = (int)ProjectSector.Retail, Residential = new { UnitType = (int)UnitType.Villa } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountLeadsAsync(OfficeClient(), lastName));
    }

    /// <summary>A full Arabic form is well over the old 4 KB cap: Arabic is two bytes a character in
    /// UTF-8, and the notes alone may be 2,000 characters.</summary>
    [Fact]
    public async Task A_long_arabic_submission_is_accepted()
    {
        string lastName = $"عربي-{Guid.NewGuid():N}";
        string notes = new('م', 2000);

        HttpResponseMessage response = await fixture.CreatePublicClient().PostAsJsonAsync(PublicLeads, new
        {
            FirstName = "منى",
            LastName = lastName,
            Notes = notes,
            Intake = new { ClientAddress = new string('ق', 500), UnitAddress = new string('ع', 500) },
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        JsonElement lead = await FindLeadAsync(OfficeClient(), lastName);
        Assert.Equal(notes, lead.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task A_body_over_32_KB_is_413()
    {
        HttpResponseMessage response = await fixture.CreatePublicClient().PostAsJsonAsync(PublicLeads, new
        {
            FirstName = "Big",
            LastName = $"Body-{Guid.NewGuid():N}",
            Notes = new string('a', 33_000),
        });

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task The_office_can_create_an_unassigned_lead_and_assign_it_later()
    {
        HttpClient office = OfficeClient(CrmPermissions.Leads.Create, CrmPermissions.Leads.Update);
        string lastName = $"Office-{Guid.NewGuid():N}";

        HttpResponseMessage created = await office.PostAsJsonAsync("/api/crm/leads", new
        {
            FirstName = "Hany",
            LastName = lastName,
            Source = (int)LeadSource.Referral,
            OwnerId = (Guid?)null,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Guid leadId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("leadId").GetGuid();

        JsonElement lead = await office.GetFromJsonAsync<JsonElement>($"/api/crm/leads/{leadId}");
        Assert.Equal(JsonValueKind.Null, lead.GetProperty("ownerId").ValueKind);

        HttpResponseMessage assigned = await office.PutAsJsonAsync($"/api/crm/leads/{leadId}", new
        {
            FirstName = "Hany",
            LastName = lastName,
            Source = (int)LeadSource.Referral,
            OwnerId = ApiFixture.SeededEmployeeId,
        });
        Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);

        lead = await office.GetFromJsonAsync<JsonElement>($"/api/crm/leads/{leadId}");
        Assert.Equal(ApiFixture.SeededEmployeeId, lead.GetProperty("ownerId").GetGuid());
    }

    [Fact]
    public async Task An_empty_owner_id_is_refused()
    {
        HttpClient office = OfficeClient(CrmPermissions.Leads.Create);

        HttpResponseMessage created = await office.PostAsJsonAsync("/api/crm/leads", new
        {
            FirstName = "Hany",
            LastName = $"EmptyOwner-{Guid.NewGuid():N}",
            Source = (int)LeadSource.Referral,
            OwnerId = Guid.Empty,
        });

        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
    }

    // --- helpers ---

    private HttpClient OfficeClient(params string[] extra)
    {
        HttpClient client = fixture.CreateClient();
        string token = fixture.IssueTestToken(Guid.NewGuid().ToString(), [CrmPermissions.Leads.Read, .. extra]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> FindLeadAsync(HttpClient office, string lastName)
    {
        JsonElement page = await SearchAsync(office, lastName);
        return Assert.Single(page.GetProperty("items").EnumerateArray());
    }

    private static async Task<int> CountLeadsAsync(HttpClient office, string lastName) =>
        (await SearchAsync(office, lastName)).GetProperty("items").GetArrayLength();

    private static Task<JsonElement> SearchAsync(HttpClient office, string lastName) =>
        office.GetFromJsonAsync<JsonElement>($"/api/crm/leads?search={Uri.EscapeDataString(lastName)}&pageSize=10");
}
