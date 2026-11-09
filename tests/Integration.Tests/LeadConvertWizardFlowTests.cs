using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// LC-1: the contract the frontend's "Convert lead" wizard depends on. The wizard turns a lead into a client,
/// a project whose stages come from a confirmed scope of work, and a draft site survey, by calling the existing
/// routes in this exact order as one office user:
///
/// <list type="number">
/// <item>client from the lead's data (<c>POST /api/clients</c>);</item>
/// <item>project, Stage workflow, no template (<c>POST /api/projects</c>);</item>
/// <item>scope of work: header, items, an edited fee, confirm;</item>
/// <item>site survey from the intake answers (<c>POST /api/sites</c>);</item>
/// <item>the lead converted into the client, last, so it stays open until everything else exists.</item>
/// </list>
///
/// <para>No route here is new. The test pins that one office user can run the whole chain: the project's
/// creator is its owner, which passes the scope-of-work manager check and the site's Editor check.</para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class LeadConvertWizardFlowTests(ApiFixture fixture)
{
    [Fact]
    public async Task One_office_user_turns_a_lead_into_a_client_a_scoped_project_and_a_draft_site()
    {
        string suffix = Guid.NewGuid().ToString("N");

        // --- The office actor: a hired employee in the admin role, logged in for real.
        string officeEmail = $"lc1-office-{suffix}@example.com";
        (_, string employeeUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, officeEmail);
        HttpClient admin = await TestUsers.AdminClientAsync(fixture);
        await ExpectAsync(HttpStatusCode.NoContent, await admin.PostAsync(
            $"/api/roles/{await TestUsers.RoleIdAsync(admin, "admin")}/users/{employeeUserId}", content: null));
        HttpClient office = (await TestUsers.LoginAsync(fixture, employeeUserId, officeEmail, IdentityTestSeeding.Password)).Client;

        // --- A website lead with its intake, as the public form leaves it (unassigned).
        string leadEmail = $"lc1-lead-{suffix}@example.com";
        await ExpectAsync(HttpStatusCode.Accepted, await fixture.CreatePublicClient().PostAsJsonAsync("/api/crm/public/leads", new
        {
            FirstName = "Mona",
            LastName = $"Lead-{suffix}",
            Email = leadEmail,
            Phone = "+201000000001",
            Intake = new
            {
                ClientAddress = "Zamalek, Cairo",
                Sector = 1, // Residential
                UnitAddress = "Villa 7, New Cairo",
                AreaSqm = 240m,
                Residential = new { UnitType = 2, UnitCondition = 2, IsLicensed = true, IsRented = false },
            },
        }));
        JsonElement page = await office.GetFromJsonAsync<JsonElement>($"/api/crm/leads?search=Lead-{suffix}");
        Guid leadId = Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid();

        // --- A stage template with two stages (the library the scope picks from).
        HttpResponseMessage templateResponse = await office.PostAsJsonAsync("/api/stage-templates", new { Name = $"LC1-{suffix}", Type = 2 });
        await ExpectAsync(HttpStatusCode.OK, templateResponse);
        Guid templateId = (await Json(templateResponse)).GetProperty("stageTemplateId").GetGuid();
        Guid designStage = await AddTemplateStageAsync(office, templateId, "Design", 1, 5000m);
        Guid fitOutStage = await AddTemplateStageAsync(office, templateId, "Fit-out", 2, 20000m);

        // 1. Client from the lead's data.
        HttpResponseMessage clientResponse = await office.PostAsJsonAsync("/api/clients", new
        {
            Email = leadEmail,
            IdentityTestSeeding.Password,
            FirstName = "Mona",
            LastName = $"Lead-{suffix}",
            Address = "Zamalek, Cairo",
            PhoneNumber = "+201000000001",
        });
        await ExpectAsync(HttpStatusCode.Created, clientResponse);
        Guid clientId = (await Json(clientResponse)).GetProperty("clientId").GetGuid();

        // 2. Project: Stage workflow, no template - the scope of work supplies the stages.
        string projectBody = $$"""
            {"code":"LC1-{{suffix[..12]}}","name":"Mona villa","address":"Villa 7, New Cairo","workflowType":1,
             "clientId":"{{clientId}}","estimatedStartDate":"2027-01-10"}
            """;
        HttpResponseMessage projectResponse = await office.PostAsync("/api/projects", new StringContent(projectBody, Encoding.UTF8, "application/json"));
        await ExpectAsync(HttpStatusCode.OK, projectResponse);
        Guid projectId = (await Json(projectResponse)).GetProperty("projectId").GetGuid();

        // 3. Scope of work: header, both stages, a changed fee, confirm.
        string scope = $"/api/projects/{projectId}/scope-of-work";
        await ExpectAsync(HttpStatusCode.NoContent, await office.PutAsJsonAsync(scope, new
        {
            DurationDays = 120,
            PlannedStartDate = "2027-01-10",
            AdjustmentAmount = -1000m,
            AdjustmentNote = "Opening discount",
            Notes = "From the website lead",
        }));
        HttpResponseMessage itemsResponse = await office.PostAsJsonAsync($"{scope}/items", new { SourceStageIds = new[] { designStage, fitOutStage } });
        await ExpectAsync(HttpStatusCode.OK, itemsResponse);
        JsonElement fitOutItem = (await Json(itemsResponse)).EnumerateArray()
            .Single(i => i.GetProperty("sourceStageId").GetGuid() == fitOutStage);
        await ExpectAsync(HttpStatusCode.NoContent, await office.PutAsJsonAsync($"{scope}/items/{fitOutItem.GetProperty("id").GetGuid()}", new
        {
            Name = "Fit-out",
            Description = "Full fit-out",
            Fee = 18000m,
        }));
        await ExpectAsync(HttpStatusCode.OK, await office.PostAsync($"{scope}/confirm", content: null));

        // 4. Site survey from the intake answers.
        HttpResponseMessage siteResponse = await office.PostAsJsonAsync("/api/sites", new
        {
            ProjectId = projectId,
            Data = new { Address = "Villa 7, New Cairo", UnitType = 2, UnitCondition = 2, AreaSqm = 240m, IsLicensed = true, IsRented = false },
        });
        await ExpectAsync(HttpStatusCode.Created, siteResponse);
        Guid siteId = (await Json(siteResponse)).GetProperty("siteId").GetGuid();

        // 5. The lead, last.
        HttpResponseMessage converted = await office.PostAsJsonAsync($"/api/crm/leads/{leadId}/convert", new { ExistingClientId = clientId });
        await ExpectAsync(HttpStatusCode.OK, converted);

        // --- What the wizard promises the user.
        JsonElement lead = await office.GetFromJsonAsync<JsonElement>($"/api/crm/leads/{leadId}");
        Assert.Equal(5, lead.GetProperty("status").GetInt32()); // Converted
        Assert.Equal(clientId, lead.GetProperty("convertedClientId").GetGuid());

        JsonElement[] stages = [.. (await office.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/stages")).EnumerateArray()
            .OrderBy(s => s.GetProperty("order").GetInt32())];
        Assert.Equal(["Design", "Fit-out"], stages.Select(s => s.GetProperty("name").GetString()));
        Assert.Equal([5000m, 18000m], stages.Select(s => s.GetProperty("cost").GetDecimal()));

        JsonElement project = await office.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}");
        Assert.Equal(clientId, project.GetProperty("clientId").GetGuid());
        Assert.StartsWith("2027-01-10", project.GetProperty("estimatedStartDate").GetString());
        Assert.StartsWith("2027-05-10", project.GetProperty("estimatedEndDate").GetString()); // 120 days on

        JsonElement site = await office.GetFromJsonAsync<JsonElement>($"/api/sites/{siteId}");
        Assert.Equal(projectId, site.GetProperty("projectId").GetGuid());
        Assert.Equal(1, site.GetProperty("status").GetInt32()); // Draft
        Assert.Equal(240m, site.GetProperty("areaSqm").GetDecimal());
        Assert.Equal(2, site.GetProperty("unitType").GetInt32());
    }

    private static async Task<Guid> AddTemplateStageAsync(HttpClient office, Guid templateId, string name, int order, decimal fee)
    {
        HttpResponseMessage response = await office.PostAsJsonAsync($"/api/stage-templates/{templateId}/stages", new
        {
            Name = name,
            Order = order,
            WeightPercent = 50m,
            Description = name,
            DefaultFee = fee,
        });
        await ExpectAsync(HttpStatusCode.OK, response);
        return (await Json(response)).GetProperty("stageId").GetGuid();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task ExpectAsync(HttpStatusCode expected, HttpResponseMessage response)
    {
        if (response.StatusCode != expected)
        {
            Assert.Fail($"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} -> {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }
}
