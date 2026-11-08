using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// The backend half of the sync run (S1–S11): additive response fields the frontend needs, the Draft-only variation
/// description edit, the catalog item detail read and the project activity writer. Every assertion goes through the
/// real route, and the activity rows through the real outbox (<see cref="DrainOutboxAsync"/>).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SyncContractRouteTests(ApiFixture fixture)
{
    private static readonly string[] ProjectCodes =
    [
        "project.create", "project.read", "project.manage_members", "clients.create", "clients.read",
        "stages.read", "stages.manage", "stages.complete", "stages.reject", "stages.manage_gates",
        "scope.read", "scope.manage", "scope.confirm",
        "stage_tasks.read", "stage_tasks.manage", "stage_tasks.assign", "stage_tasks.complete", "stage_tasks.manage_gates",
        "documents.read", "documents.upload", "approvals.read", "activities.read",
    ];

    /// <summary>S1: a task's notes come back on the task read, so an edit that resends the read does not wipe them.</summary>
    [Fact]
    public async Task S1_task_response_carries_notes()
    {
        (HttpClient owner, _, _) = await OwnerAsync("s1");
        Guid projectId = await ProjectTestSeeding.CreateFreeStyleProjectAsync(owner, Code("S1"));

        Guid taskId = await CreateTaskAsync(owner, projectId, "Noted task", notes: "Keep these notes");

        JsonElement task = await owner.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/tasks/{taskId}");
        Assert.Equal("Keep these notes", task.GetProperty("notes").GetString());

        JsonElement list = await owner.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/tasks");
        Assert.Equal("Keep these notes", Assert.Single(list.EnumerateArray()).GetProperty("notes").GetString());
    }

    /// <summary>S2: a gate's resolution (who, when, the note) and its referenced document are on the gate read.</summary>
    [Fact]
    public async Task S2_gate_response_carries_resolution_fields()
    {
        (HttpClient owner, _, _) = await OwnerAsync("s2");
        Guid projectId = await ProjectTestSeeding.CreateFreeStyleProjectAsync(owner, Code("S2"));
        Guid taskId = await CreateTaskAsync(owner, projectId, "Gated task");

        HttpResponseMessage created = await owner.PostAsJsonAsync($"/api/projects/{projectId}/tasks/{taskId}/gates",
            new { ConditionType = 4, Trigger = 2, Label = "Site checked" });
        Assert.True(created.StatusCode == HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        Guid gateId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("gateConditionId").GetGuid();

        HttpResponseMessage satisfied = await owner.PostAsJsonAsync(
            $"/api/projects/{projectId}/gates/{gateId}/satisfy", new { Note = "Checked on site" });
        Assert.True(satisfied.StatusCode == HttpStatusCode.NoContent, await satisfied.Content.ReadAsStringAsync());

        JsonElement gates = await owner.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/tasks/{taskId}/gates");
        JsonElement gate = Assert.Single(gates.EnumerateArray());
        Assert.Equal("Checked on site", gate.GetProperty("resolutionNote").GetString());
        Assert.Equal(JsonValueKind.String, gate.GetProperty("resolvedById").ValueKind);
        Assert.Equal(JsonValueKind.String, gate.GetProperty("resolvedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, gate.GetProperty("referencedDocumentId").ValueKind);
    }

    /// <summary>S3: an approval requested for a project's gate names that project on the approver's inbox row.</summary>
    [Fact]
    public async Task S3_pending_approval_carries_its_project()
    {
        (HttpClient owner, _, _) = await OwnerAsync("s3");
        Guid projectId = await ProjectTestSeeding.CreateFreeStyleProjectAsync(owner, Code("S3"));
        Guid taskId = await CreateTaskAsync(owner, projectId, "Approval task");

        HttpResponseMessage created = await owner.PostAsJsonAsync($"/api/projects/{projectId}/tasks/{taskId}/gates",
            new { ConditionType = 3, Trigger = 2, Label = "Manager sign-off", ApprovalType = 1 });
        Assert.True(created.StatusCode == HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        Guid gateId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("gateConditionId").GetGuid();

        JsonElement inbox = await owner.GetFromJsonAsync<JsonElement>("/api/approvals/pending?pageNumber=1&pageSize=200");
        JsonElement row = Assert.Single(inbox.GetProperty("items").EnumerateArray(),
            r => r.GetProperty("subjectId").GetGuid() == gateId);
        Assert.Equal(projectId, row.GetProperty("projectId").GetGuid());
    }

    /// <summary>S4: a Draft variation's description is editable (204); once submitted it is a 409.</summary>
    [Fact]
    public async Task S4_variation_description_is_editable_only_in_draft()
    {
        (HttpClient owner, _, _) = await OwnerAsync("s4", FinanceCodes);
        Guid projectId = await ProjectTestSeeding.CreateFreeStyleProjectAsync(owner, Code("S4"));
        (Guid budgetLineId, _) = await CreateBudgetLineAsync(owner, projectId, "Joinery");

        HttpResponseMessage raised = await owner.PostAsJsonAsync("/api/variations", new
        {
            Kind = 2,
            ProjectId = projectId,
            Reason = "Scope grew",
            BudgetLineId = budgetLineId,
            Amount = 250m,
            Description = "First draft",
        });
        Assert.True(raised.StatusCode == HttpStatusCode.Created, await raised.Content.ReadAsStringAsync());
        Guid variationId = (await raised.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("variationId").GetGuid();

        HttpResponseMessage edited = await owner.PutAsJsonAsync(
            $"/api/variations/{variationId}/description", new { Description = "Second draft" });
        Assert.True(edited.StatusCode == HttpStatusCode.NoContent, await edited.Content.ReadAsStringAsync());
        JsonElement variation = await owner.GetFromJsonAsync<JsonElement>($"/api/variations/{variationId}");
        Assert.Equal("Second draft", variation.GetProperty("description").GetString());

        Assert.Equal(HttpStatusCode.NoContent,
            (await owner.PutAsJsonAsync($"/api/variations/{variationId}/submit", new { })).StatusCode);
        HttpResponseMessage late = await owner.PutAsJsonAsync(
            $"/api/variations/{variationId}/description", new { Description = "Too late" });
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Contains("variations.InvalidStatusTransition", await late.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await owner.PutAsJsonAsync(
            $"/api/variations/{Guid.NewGuid()}/description", new { Description = "x" })).StatusCode);

        // Same permission as raise.
        HttpClient reader = fixture.CreateClient();
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.IssueTestToken(Guid.NewGuid().ToString(), "finance.read"));
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PutAsJsonAsync(
            $"/api/variations/{variationId}/description", new { Description = "x" })).StatusCode);
    }

    /// <summary>S5: a budget line's cost category name is on its detail and on the by-project list.</summary>
    [Fact]
    public async Task S5_budget_lines_carry_the_cost_category_name()
    {
        (HttpClient owner, _, _) = await OwnerAsync("s5", FinanceCodes);
        Guid projectId = await ProjectTestSeeding.CreateFreeStyleProjectAsync(owner, Code("S5"));
        (Guid budgetLineId, string categoryName) = await CreateBudgetLineAsync(owner, projectId, "Electrics");

        JsonElement line = await owner.GetFromJsonAsync<JsonElement>($"/api/budget-lines/{budgetLineId}");
        Assert.Equal(categoryName, line.GetProperty("costCategoryName").GetString());

        JsonElement list = await owner.GetFromJsonAsync<JsonElement>($"/api/budget-lines/by-project/{projectId}");
        Assert.Equal(categoryName, Assert.Single(list.EnumerateArray()).GetProperty("costCategoryName").GetString());
    }

    /// <summary>S6: the project read and every row of the project list carry the client's and owner's names.</summary>
    [Fact]
    public async Task S6_projects_carry_client_and_owner_names()
    {
        (HttpClient owner, _, _) = await OwnerAsync("s6", firstName: "Olive", lastName: "Owner");
        string code = Code("S6");
        Guid projectId = await ProjectTestSeeding.CreateFreeStyleProjectAsync(owner, code);
        Guid secondId = await ProjectTestSeeding.CreateFreeStyleProjectAsync(owner, code + "B");

        JsonElement project = await owner.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}");
        Assert.Equal("Portal Client", project.GetProperty("clientName").GetString());
        Assert.Equal("Olive Owner", project.GetProperty("ownerName").GetString());

        JsonElement page = await owner.GetFromJsonAsync<JsonElement>($"/api/projects?pageNumber=1&pageSize=50&search={code}");
        List<JsonElement> rows = [.. page.GetProperty("items").EnumerateArray()];
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.GetProperty("id").GetGuid() == secondId);
        Assert.All(rows, r =>
        {
            Assert.Equal("Portal Client", r.GetProperty("clientName").GetString());
            Assert.Equal("Olive Owner", r.GetProperty("ownerName").GetString());
        });
    }

    /// <summary>S7: a ledger list row says whether the entry has been reversed, and carries its note.</summary>
    [Fact]
    public async Task S7_ledger_list_rows_carry_reversal_flag_and_note()
    {
        (HttpClient owner, _, _) = await OwnerAsync("s7", FinanceCodes);
        Guid projectId = await ProjectTestSeeding.CreateFreeStyleProjectAsync(owner, Code("S7"));

        HttpResponseMessage posted = await owner.PostAsJsonAsync("/api/ledger-entries", new
        {
            ProjectId = projectId,
            Amount = 300m,
            Direction = 2,
            Type = 2,
            OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            VendorName = "Timber Co",
            Note = "Deposit",
        });
        Assert.True(posted.StatusCode == HttpStatusCode.Created, await posted.Content.ReadAsStringAsync());
        Guid entryId = (await posted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ledgerEntryId").GetGuid();

        HttpResponseMessage reversed = await owner.PostAsJsonAsync($"/api/ledger-entries/{entryId}/reverse", new { Note = "Posted twice" });
        Assert.True(reversed.StatusCode == HttpStatusCode.Created, await reversed.Content.ReadAsStringAsync());
        Guid mirrorId = (await reversed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ledgerEntryId").GetGuid();

        JsonElement page = await owner.GetFromJsonAsync<JsonElement>($"/api/ledger-entries/by-project/{projectId}");
        List<JsonElement> rows = [.. page.GetProperty("items").EnumerateArray()];
        JsonElement original = Assert.Single(rows, r => r.GetProperty("id").GetGuid() == entryId);
        JsonElement mirror = Assert.Single(rows, r => r.GetProperty("id").GetGuid() == mirrorId);
        Assert.True(original.GetProperty("isReversed").GetBoolean());
        Assert.Equal("Deposit", original.GetProperty("note").GetString());
        Assert.False(mirror.GetProperty("isReversed").GetBoolean());
        Assert.Equal("Posted twice", mirror.GetProperty("note").GetString());
    }

    /// <summary>S8: a client list row carries the portal login's phone number.</summary>
    [Fact]
    public async Task S8_client_list_rows_carry_the_phone_number()
    {
        (HttpClient owner, _, _) = await OwnerAsync("s8", ["crm.clients.read_all"]);
        string address = $"S8 {Guid.NewGuid():N}";
        HttpResponseMessage created = await owner.PostAsJsonAsync("/api/clients", new
        {
            Email = $"s8-{Guid.NewGuid():N}@example.com",
            password = IdentityTestSeeding.Password,
            FirstName = "Phone",
            LastName = "Client",
            Address = address,
            PhoneNumber = "+20 100 555 0101",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        JsonElement page = await owner.GetFromJsonAsync<JsonElement>($"/api/clients?isActive=all&search={Uri.EscapeDataString(address)}");
        JsonElement row = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal("+20 100 555 0101", row.GetProperty("phoneNumber").GetString());
    }

    /// <summary>S9 and S10: catalog categories and items carry descriptions and image; one item reads with its
    /// attribute values, and a missing item is a 404.</summary>
    [Fact]
    public async Task S9_S10_catalog_descriptions_and_item_detail()
    {
        (HttpClient admin, _, _) = await OwnerAsync("s9", ["crm.catalog.read", "crm.catalog.manage"]);
        string s = Guid.NewGuid().ToString("N")[..8];
        const string c = "/api/crm/catalog";

        HttpResponseMessage cat = await admin.PostAsJsonAsync($"{c}/categories", new
        {
            Code = $"C{s}",
            NameEn = "Cabinets",
            NameAr = "خزائن",
            DescriptionEn = "All cabinets",
            DescriptionAr = "كل الخزائن",
            ImageUri = "https://img.example/cab.png",
        });
        Assert.Equal(HttpStatusCode.Created, cat.StatusCode);
        Guid category = (await cat.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("categoryId").GetGuid();

        HttpResponseMessage it = await admin.PostAsJsonAsync($"{c}/items", new { CategoryId = category, Code = $"I{s}", NameEn = "Base", NameAr = "قاعدة" });
        Assert.Equal(HttpStatusCode.Created, it.StatusCode);
        Guid item = (await it.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("itemId").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{c}/items/{item}", new
        {
            Code = $"I{s}",
            NameEn = "Base",
            NameAr = "قاعدة",
            DescriptionEn = "60cm base",
            DescriptionAr = "قاعدة ٦٠",
            ImageUri = "https://img.example/base.png",
        })).StatusCode);

        HttpResponseMessage attr = await admin.PostAsJsonAsync($"{c}/attributes", new
        {
            CategoryId = category,
            Code = $"A{s}",
            NameEn = "Width",
            NameAr = "عرض",
            DataType = 2,
        });
        Assert.Equal(HttpStatusCode.Created, attr.StatusCode);
        Guid attribute = (await attr.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("attributeId").GetGuid();
        HttpResponseMessage values = await admin.PutAsJsonAsync($"{c}/items/{item}/attribute-values",
            new { Values = new[] { new { AttributeId = attribute, ValueNumber = 60m } } });
        Assert.True(values.StatusCode == HttpStatusCode.NoContent, await values.Content.ReadAsStringAsync());

        JsonElement tree = await admin.GetFromJsonAsync<JsonElement>($"{c}/tree");
        JsonElement treeRow = Assert.Single(tree.EnumerateArray(), r => r.GetProperty("id").GetGuid() == category);
        Assert.Equal("All cabinets", treeRow.GetProperty("descriptionEn").GetString());
        Assert.Equal("كل الخزائن", treeRow.GetProperty("descriptionAr").GetString());
        Assert.Equal("https://img.example/cab.png", treeRow.GetProperty("imageUri").GetString());

        JsonElement items = await admin.GetFromJsonAsync<JsonElement>($"{c}/categories/{category}/items");
        JsonElement listRow = Assert.Single(items.EnumerateArray());
        Assert.Equal("60cm base", listRow.GetProperty("descriptionEn").GetString());
        Assert.Equal("https://img.example/base.png", listRow.GetProperty("imageUri").GetString());

        HttpClient reader = fixture.CreateClient();
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.IssueTestToken(Guid.NewGuid().ToString(), "crm.catalog.read"));
        HttpResponseMessage detailResponse = await reader.GetAsync($"{c}/items/{item}");
        Assert.True(detailResponse.StatusCode == HttpStatusCode.OK, await detailResponse.Content.ReadAsStringAsync());
        JsonElement detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(item, detail.GetProperty("id").GetGuid());
        Assert.Equal(category, detail.GetProperty("categoryId").GetGuid());
        Assert.Equal("قاعدة ٦٠", detail.GetProperty("descriptionAr").GetString());
        JsonElement value = Assert.Single(detail.GetProperty("attributeValues").EnumerateArray());
        Assert.Equal(attribute, value.GetProperty("attributeId").GetGuid());
        Assert.Equal(60m, value.GetProperty("valueNumber").GetDecimal());

        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"{c}/items/{Guid.NewGuid()}")).StatusCode);

        HttpClient outsider = fixture.CreateClient();
        outsider.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.IssueTestToken(Guid.NewGuid().ToString(), "crm.catalog.manage"));
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync($"{c}/items/{item}")).StatusCode);
    }

    /// <summary>S11: task writes (create, update, assign, start, comment, document upload) land on the task's
    /// timeline through the outbox, with the actor's name, the entity's name and the project.</summary>
    [Fact]
    public async Task S11_task_writes_are_recorded_as_activities()
    {
        (HttpClient owner, Guid ownerEmployeeId, string ownerUserId) =
            await OwnerAsync("s11t", firstName: "Sara", lastName: "Activity");
        Guid projectId = await ProjectTestSeeding.CreateFreeStyleProjectAsync(owner, Code("S11T"));
        Guid taskId = await CreateTaskAsync(owner, projectId, "Timeline task");

        HttpResponseMessage updated = await owner.PutAsJsonAsync($"/api/projects/{projectId}/tasks/{taskId}",
            new { Name = "Timeline task", Notes = "edited" });
        Assert.True(updated.StatusCode == HttpStatusCode.NoContent, await updated.Content.ReadAsStringAsync());
        HttpResponseMessage assigned = await owner.PostAsJsonAsync($"/api/projects/{projectId}/tasks/{taskId}/assign",
            new { EmployeeId = ownerEmployeeId });
        Assert.True(assigned.StatusCode == HttpStatusCode.NoContent, await assigned.Content.ReadAsStringAsync());
        HttpResponseMessage started = await owner.PostAsync($"/api/projects/{projectId}/tasks/{taskId}/start", content: null);
        Assert.True(started.StatusCode == HttpStatusCode.NoContent, await started.Content.ReadAsStringAsync());
        HttpResponseMessage commented = await owner.PostAsJsonAsync($"/api/projects/{projectId}/tasks/{taskId}/comments",
            new { Content = "On it" });
        Assert.True(commented.StatusCode == HttpStatusCode.OK, await commented.Content.ReadAsStringAsync());
        HttpResponseMessage uploaded = await owner.PostAsync($"/api/project-tasks/{taskId}/documents", File("spec.txt"));
        Assert.True(uploaded.StatusCode == HttpStatusCode.OK, await uploaded.Content.ReadAsStringAsync());

        await DrainOutboxAsync();

        JsonElement page = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/activities?entityName=ProjectTask&entityId={taskId}&pageSize=100");
        List<JsonElement> rows = [.. page.GetProperty("items").EnumerateArray()];
        Assert.Equal(6, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal(ownerUserId, r.GetProperty("actorUserId").GetString());
            Assert.Equal("Sara Activity", r.GetProperty("actorName").GetString());
            Assert.Equal("ProjectTask", r.GetProperty("entityName").GetString());
            Assert.Equal(taskId.ToString(), r.GetProperty("entityId").GetString());
            Assert.Equal("Timeline task", r.GetProperty("entityDisplayName").GetString());
            Assert.Equal(projectId, r.GetProperty("projectId").GetGuid());
            Assert.False(string.IsNullOrWhiteSpace(r.GetProperty("message").GetString()));
        });
        Assert.Single(rows, r => r.GetProperty("action").GetInt32() == 1);
        Assert.Equal(5, rows.Count(r => r.GetProperty("action").GetInt32() == 2));
        Assert.Contains(rows, r => r.GetProperty("message").GetString()!.Contains("spec.txt", StringComparison.Ordinal));

        // The project timeline holds them too, and a second drain writes nothing twice.
        await DrainOutboxAsync();
        JsonElement project = await owner.GetFromJsonAsync<JsonElement>($"/api/activities?projectId={projectId}&pageSize=100");
        Assert.Equal(6, project.GetProperty("totalCount").GetInt32());
    }

    /// <summary>S11: stage writes (create, update, reject) land on the stage's timeline.</summary>
    [Fact]
    public async Task S11_stage_writes_are_recorded_as_activities()
    {
        (HttpClient owner, _, string ownerUserId) = await OwnerAsync("s11s", firstName: "Stan", lastName: "Stage");
        (Guid projectId, _, _) = await ProjectTestSeeding.CreateConfirmedStageProjectAsync(owner, Code("S11S"));

        HttpResponseMessage created = await owner.PostAsJsonAsync($"/api/projects/{projectId}/stages", new { Name = "Handover", Order = 99 });
        Assert.True(created.StatusCode == HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        Guid stageId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("stageId").GetGuid();
        HttpResponseMessage updated = await owner.PutAsJsonAsync($"/api/projects/{projectId}/stages/{stageId}",
            new { Name = "Handover", Notes = "keys" });
        Assert.True(updated.StatusCode == HttpStatusCode.NoContent, await updated.Content.ReadAsStringAsync());
        HttpResponseMessage uploaded = await owner.PostAsync($"/api/project-stages/{stageId}/documents", File("handover.txt"));
        Assert.True(uploaded.StatusCode == HttpStatusCode.OK, await uploaded.Content.ReadAsStringAsync());

        await DrainOutboxAsync();

        JsonElement page = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/activities?entityName=ProjectStage&entityId={stageId}&pageSize=100");
        List<JsonElement> rows = [.. page.GetProperty("items").EnumerateArray()];
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal(ownerUserId, r.GetProperty("actorUserId").GetString());
            Assert.Equal("Stan Stage", r.GetProperty("actorName").GetString());
            Assert.Equal("Handover", r.GetProperty("entityDisplayName").GetString());
            Assert.Equal(projectId, r.GetProperty("projectId").GetGuid());
        });
        Assert.Single(rows, r => r.GetProperty("action").GetInt32() == 1);
    }

    private static readonly string[] FinanceCodes =
        ["finance.read", "finance.manage_cost_category", "finance.manage_budget", "finance.manage_variation",
         "finance.record_payment", "finance.reverse_payment"];

    private static string Code(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..12].ToUpperInvariant();

    private async Task<(HttpClient Client, Guid EmployeeId, string UserId)> OwnerAsync(
        string prefix, string[]? extraCodes = null, string firstName = "Test", string lastName = "User")
    {
        (Guid employeeId, string userId) = await HrTestSeeding.HireEmployeeOverHttpAsync(
            fixture, $"{prefix}-{Guid.NewGuid():N}@example.com", firstName: firstName, lastName: lastName);
        HttpClient client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.IssueTestToken(userId, [.. ProjectCodes, .. extraCodes ?? []]));
        return (client, employeeId, userId);
    }

    private static async Task<Guid> CreateTaskAsync(HttpClient owner, Guid projectId, string name, string? notes = null)
    {
        HttpResponseMessage task = await owner.PostAsJsonAsync($"/api/projects/{projectId}/tasks", new { Name = name, Notes = notes });
        Assert.True(task.StatusCode == HttpStatusCode.OK, await task.Content.ReadAsStringAsync());
        return (await task.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("taskId").GetGuid();
    }

    private static async Task<(Guid BudgetLineId, string CategoryName)> CreateBudgetLineAsync(HttpClient owner, Guid projectId, string name)
    {
        string categoryName = $"Cat {Guid.NewGuid():N}"[..14];
        HttpResponseMessage category = await owner.PostAsJsonAsync("/api/cost-categories", new
        {
            Code = $"CC{Guid.NewGuid():N}"[..10],
            Name = categoryName,
        });
        Assert.True(category.IsSuccessStatusCode, await category.Content.ReadAsStringAsync());
        Guid categoryId = (await category.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("costCategoryId").GetGuid();

        HttpResponseMessage line = await owner.PostAsJsonAsync("/api/budget-lines", new
        {
            Code = "BL-1",
            ProjectId = projectId,
            CostCategoryId = categoryId,
            Name = name,
            AllocatedAmount = 1000m,
        });
        Assert.True(line.IsSuccessStatusCode, await line.Content.ReadAsStringAsync());
        return ((await line.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("budgetLineId").GetGuid(), categoryName);
    }

    private static MultipartFormDataContent File(string name)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes($"contents of {name}"));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        return new MultipartFormDataContent { { content, "file", name } };
    }

    private async Task DrainOutboxAsync()
    {
        OutboxProcessor processor = fixture.Services.GetServices<IHostedService>().OfType<OutboxProcessor>().Single();
        for (int i = 0; i < 5; i++)
        {
            await processor.ProcessPendingAsync(CancellationToken.None);
        }
    }
}
