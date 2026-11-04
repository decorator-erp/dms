using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Jobs;
using BuildingBlocks.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// E2E (CRM production review): the whole client loop through the real host, with real logins. The office
/// actor is a hired employee put in the seeded <c>admin</c> role; the client is the portal login that
/// <c>POST /api/clients</c> provisions, in the seeded <c>client</c> role. Every step is its HTTP route except
/// the two things no route does: delivering the outbox (<see cref="OutboxProcessor"/>) and the clock closing a
/// period (<c>CrmPeriodCloseJob</c>, run from the host's DI after the period's close time is moved into the
/// past).
///
/// <para><b>Order.</b> Scope of work comes right after project create, not last: confirm refuses a project
/// that already has stages (409 <c>projects.scope.ProjectHasStages</c>), and the brief, the period and the
/// gates all hang on a stage. So the confirmed scope of work is what creates the stage the rest of the loop
/// runs on.</para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CrmEndToEndFlowTests(ApiFixture fixture)
{
    private const int Satisfied = 2;
    private const int Pending = 1;

    [Fact]
    public async Task Lead_to_confirmed_scope_brief_items_and_meeting_with_the_client_deciding_in_the_portal()
    {
        string suffix = Guid.NewGuid().ToString("N");

        // --- The office actor: a hired employee in the admin role, logged in for real.
        string officeEmail = $"e2e-office-{suffix}@example.com";
        (Guid employeeId, string employeeUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, officeEmail);
        HttpClient admin = await TestUsers.AdminClientAsync(fixture);
        await ExpectAsync(HttpStatusCode.NoContent, await admin.PostAsync(
            $"/api/roles/{await TestUsers.RoleIdAsync(admin, "admin")}/users/{employeeUserId}", content: null));
        HttpClient office = (await TestUsers.LoginAsync(fixture, employeeUserId, officeEmail, IdentityTestSeeding.Password)).Client;

        // --- The client, its primary contact, and its portal login.
        string clientEmail = $"e2e-client-{suffix}@example.com";
        HttpResponseMessage clientResponse = await office.PostAsJsonAsync("/api/clients", new
        {
            Email = clientEmail,
            password = IdentityTestSeeding.Password,
            FirstName = "End",
            LastName = "Client",
            Address = "1 Flow Rd",
        });
        await ExpectAsync(HttpStatusCode.Created, clientResponse);
        Guid clientId = (await Json(clientResponse)).GetProperty("clientId").GetGuid();
        string clientUserId = (await office.GetFromJsonAsync<JsonElement>($"/api/clients/{clientId}")).GetProperty("appUserId").GetString()!;
        await ExpectAsync(HttpStatusCode.Created, await office.PostAsJsonAsync($"/api/crm/clients/{clientId}/contacts", new
        {
            FirstName = "End",
            LastName = "Client",
            Role = 1, // Owner
            IsPrimary = true,
            Email = clientEmail,
        }));
        await DrainAsync(); // ClientCreatedIntegrationEvent activates the portal login
        HttpClient portal = (await TestUsers.LoginAsync(fixture, clientUserId, clientEmail, IdentityTestSeeding.Password)).Client;

        // --- Lead -> intake -> convert into the client.
        HttpResponseMessage leadResponse = await office.PostAsJsonAsync("/api/crm/leads", new
        {
            FirstName = "End",
            LastName = "Client",
            Source = 2, // Referral
            OwnerId = employeeId,
            Email = clientEmail,
        });
        await ExpectAsync(HttpStatusCode.Created, leadResponse);
        Guid leadId = (await Json(leadResponse)).GetProperty("leadId").GetGuid();
        await ExpectAsync(HttpStatusCode.NoContent, await office.PutAsJsonAsync($"/api/crm/leads/{leadId}/intake", new
        {
            JobTitle = "Architect",
            UnitAddress = "Flat 4, 1 Flow Rd",
            AreaSqm = 120m,
        }));
        HttpResponseMessage converted = await office.PostAsJsonAsync($"/api/crm/leads/{leadId}/convert", new { ExistingClientId = clientId });
        await ExpectAsync(HttpStatusCode.OK, converted);
        Assert.Equal(clientId, (await Json(converted)).GetProperty("clientId").GetGuid());

        // --- Project create with date-only estimated dates (H-dates).
        HttpResponseMessage templateResponse = await office.PostAsJsonAsync("/api/stage-templates", new { Name = $"E2E-{suffix}", Type = 2 });
        await ExpectAsync(HttpStatusCode.OK, templateResponse);
        Guid templateId = (await Json(templateResponse)).GetProperty("stageTemplateId").GetGuid();
        HttpResponseMessage templateStageResponse = await office.PostAsJsonAsync($"/api/stage-templates/{templateId}/stages", new
        {
            Name = "Design",
            Order = 1,
            WeightPercent = 100m,
            Description = "Concept design",
            DefaultFee = 5000m,
        });
        await ExpectAsync(HttpStatusCode.OK, templateStageResponse);
        Guid templateStageId = (await Json(templateStageResponse)).GetProperty("stageId").GetGuid();

        string projectBody = $$"""
            {"code":"E2E-{{suffix}}","name":"End to end","address":"1 Flow Rd","workflowType":1,"clientId":"{{clientId}}",
             "estimatedStartDate":"2026-12-01","estimatedEndDate":"2027-06-30"}
            """;
        HttpResponseMessage projectResponse = await office.PostAsync("/api/projects", new StringContent(projectBody, Encoding.UTF8, "application/json"));
        await ExpectAsync(HttpStatusCode.OK, projectResponse);
        Guid projectId = (await Json(projectResponse)).GetProperty("projectId").GetGuid();

        // --- Scope of work: add the template stage, confirm. The confirm creates the project's stage.
        await ExpectAsync(HttpStatusCode.NoContent, await office.PutAsJsonAsync($"/api/projects/{projectId}/scope-of-work", new
        {
            DurationDays = 90,
            PlannedStartDate = "2026-12-01",
            AdjustmentAmount = 0m,
        }));
        await ExpectAsync(HttpStatusCode.OK, await office.PostAsJsonAsync(
            $"/api/projects/{projectId}/scope-of-work/items", new { SourceStageIds = new[] { templateStageId } }));
        await ExpectAsync(HttpStatusCode.OK, await office.PostAsync($"/api/projects/{projectId}/scope-of-work/confirm", content: null));
        JsonElement scope = await office.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/scope-of-work");
        Assert.NotEqual(JsonValueKind.Null, scope.GetProperty("confirmedAtUtc").ValueKind);
        Guid stageId = Assert.Single((await office.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/stages")).EnumerateArray())
            .GetProperty("id").GetGuid();

        Guid briefGateId = await CreateStageGateAsync(office, projectId, stageId, conditionType: 8, "Brief approved");
        Guid itemsGateId = await CreateStageGateAsync(office, projectId, stageId, conditionType: 9, "Items resolved");
        Guid periodGateId = await CreateStageGateAsync(office, projectId, stageId, conditionType: 7, "Period closed");

        // --- Brief -> send a round -> the client approves it in the portal -> the brief gate is satisfied.
        HttpResponseMessage briefResponse = await office.PostAsJsonAsync($"/api/crm/projects/{projectId}/brief", new
        {
            ProjectStageId = stageId,
            Data = new { UnitType = 1, UnitCondition = 1, Address = "Flat 4, 1 Flow Rd" },
        });
        await ExpectAsync(HttpStatusCode.Created, briefResponse);
        Guid briefId = (await Json(briefResponse)).GetProperty("briefId").GetGuid();
        await ExpectAsync(HttpStatusCode.NoContent, await office.PutAsync($"/api/crm/briefs/{briefId}/send", content: null));

        Guid briefApprovalId = await PendingPortalApprovalAsync(portal, subjectId: briefId);
        await ExpectAsync(HttpStatusCode.NoContent, await portal.PutAsJsonAsync(
            $"/api/crm/portal/approvals/{briefApprovalId}/decide", new { Decision = 1, Comment = "Looks right" }));
        await DrainAsync();
        await DrainAsync();
        Assert.Equal(3, (await office.GetFromJsonAsync<JsonElement>($"/api/crm/stages/{stageId}/brief")).GetProperty("status").GetInt32());
        Assert.Equal(Satisfied, await GateStatusAsync(office, projectId, stageId, briefGateId));

        // --- Period open -> item -> send -> the client approves it in the portal.
        HttpResponseMessage periodResponse = await office.PostAsJsonAsync($"/api/crm/projects/{projectId}/periods", new
        {
            ProjectStageId = stageId,
            ClosesAtUtc = DateTime.UtcNow.AddDays(7),
        });
        await ExpectAsync(HttpStatusCode.Created, periodResponse);
        Guid periodId = (await Json(periodResponse)).GetProperty("periodId").GetGuid();
        HttpResponseMessage itemResponse = await office.PostAsJsonAsync($"/api/crm/periods/{periodId}/items", new
        {
            ProjectId = projectId,
            Category = 1, // SpaceLayout
            Title = "Open kitchen",
            Priority = 2, // Medium
        });
        await ExpectAsync(HttpStatusCode.Created, itemResponse);
        Guid itemId = (await Json(itemResponse)).GetProperty("itemId").GetGuid();
        await ExpectAsync(HttpStatusCode.NoContent, await office.PutAsync($"/api/crm/requirement-items/{itemId}/send", content: null));

        Guid itemApprovalId = await PendingPortalApprovalAsync(portal, subjectId: itemId);
        await ExpectAsync(HttpStatusCode.NoContent, await portal.PutAsJsonAsync(
            $"/api/crm/portal/approvals/{itemApprovalId}/decide", new { Decision = 1 }));
        await DrainAsync();
        await DrainAsync();
        Assert.Equal(Pending, await GateStatusAsync(office, projectId, stageId, periodGateId));

        // --- The clock closes the period: the job, run after the close time has passed.
        await ExpirePeriodAsync(periodId);
        await RunPeriodCloseJobAsync();
        await DrainAsync();
        await DrainAsync();
        Assert.Equal(2, (await office.GetFromJsonAsync<JsonElement>($"/api/crm/periods/{periodId}")).GetProperty("status").GetInt32()); // Closed
        Assert.Equal(Satisfied, await GateStatusAsync(office, projectId, stageId, periodGateId));
        Assert.Equal(Satisfied, await GateStatusAsync(office, projectId, stageId, itemsGateId));

        // --- Meeting: the office proposes, the client accepts the slot in the portal.
        DateTime start = DateTime.UtcNow.Date.AddDays(10).AddHours(9);
        HttpResponseMessage meetingResponse = await office.PostAsJsonAsync("/api/crm/meetings/propose", new
        {
            ProjectId = projectId,
            Title = "Design review",
            Type = 1, // InOffice
            Slots = new[] { new { StartsAtUtc = start, EndsAtUtc = start.AddHours(1) } },
        });
        await ExpectAsync(HttpStatusCode.Created, meetingResponse);
        Guid meetingId = (await Json(meetingResponse)).GetProperty("meetingId").GetGuid();
        JsonElement meeting = await office.GetFromJsonAsync<JsonElement>($"/api/crm/meetings/{meetingId}");
        Guid slotId = Assert.Single(meeting.GetProperty("slots").EnumerateArray()).GetProperty("id").GetGuid();
        await ExpectAsync(HttpStatusCode.NoContent, await portal.PutAsync($"/api/crm/portal/meetings/slots/{slotId}/accept", content: null));
        meeting = await office.GetFromJsonAsync<JsonElement>($"/api/crm/meetings/{meetingId}");
        Assert.Equal(2, meeting.GetProperty("status").GetInt32()); // Scheduled

        // --- The project page reads it all back in one call (B2-be).
        JsonElement details = await office.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/details");
        Assert.Equal("End Client", details.GetProperty("project").GetProperty("clientName").GetString());
        JsonElement stage = Assert.Single(details.GetProperty("stages").EnumerateArray());
        Assert.Equal(3, stage.GetProperty("gates").GetProperty("satisfied").GetInt32());
        Assert.Equal(0, stage.GetProperty("gates").GetProperty("pending").GetInt32());
    }

    private static async Task<Guid> PendingPortalApprovalAsync(HttpClient portal, Guid subjectId)
    {
        HttpResponseMessage list = await portal.GetAsync("/api/crm/portal/approvals?status=pending");
        await ExpectAsync(HttpStatusCode.OK, list);
        JsonElement row = Assert.Single(
            (await Json(list)).GetProperty("items").EnumerateArray(), r => r.GetProperty("subjectId").GetGuid() == subjectId);
        return row.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateStageGateAsync(HttpClient client, Guid projectId, Guid stageId, int conditionType, string label)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync($"/api/projects/{projectId}/stages/{stageId}/gates", new
        {
            ConditionType = conditionType,
            Trigger = 1, // Unlock
            Label = label,
        });
        await ExpectAsync(HttpStatusCode.OK, response);
        return (await Json(response)).GetProperty("gateConditionId").GetGuid();
    }

    private static async Task<int> GateStatusAsync(HttpClient client, Guid projectId, Guid stageId, Guid gateId) =>
        (await client.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/stages/{stageId}/gates"))
            .EnumerateArray().Single(g => g.GetProperty("id").GetGuid() == gateId).GetProperty("status").GetInt32();

    private Task DrainAsync() =>
        fixture.Services.GetServices<IHostedService>().OfType<OutboxProcessor>().Single().ProcessPendingAsync(CancellationToken.None);

    /// <summary>Moves the period's close time into the past, as the clock eventually would.</summary>
    private async Task ExpirePeriodAsync(Guid periodId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """UPDATE crm.crm_requirement_periods SET "ClosesAtUtc" = now() - interval '1 hour' WHERE "Id" = @id""";
        command.Parameters.AddWithValue("id", periodId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private async Task RunPeriodCloseJobAsync()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IPeriodicJob job = scope.ServiceProvider.GetServices<IPeriodicJob>().Single(j => j.Name == "CrmPeriodCloseJob");
        await job.RunAsync(CancellationToken.None);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task ExpectAsync(HttpStatusCode expected, HttpResponseMessage response) =>
        Assert.True(
            response.StatusCode == expected,
            $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery}: expected "
            + $"{(int)expected}, got {(int)response.StatusCode}. {await response.Content.ReadAsStringAsync()}");
}
