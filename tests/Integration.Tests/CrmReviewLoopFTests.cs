using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>PR F review loop 1: one test per finding fixed red before green (FINDING-F-1 … F-5).</summary>
[Collection(ApiCollection.Name)]
public sealed class CrmReviewLoopFTests(ApiFixture fixture)
{
    /// <summary>F-1: catalog update lengths match the columns (400, not a 500 from Postgres) and a taken code is 409.</summary>
    [Fact]
    public async Task F1_catalog_update_too_long_is_400_and_a_taken_code_is_409()
    {
        string s = Guid.NewGuid().ToString("N")[..8];
        (_, string userId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"rf1-{s}@example.com");
        HttpClient admin = CrmPortalTestSeeding.Client(fixture, userId, "crm.catalog.read", "crm.catalog.manage");
        Guid a = await IdAsync(await admin.PostAsJsonAsync("/api/crm/catalog/categories", new { Code = $"A{s}", NameEn = "A", NameAr = "A" }), "categoryId");
        await IdAsync(await admin.PostAsJsonAsync("/api/crm/catalog/categories", new { Code = $"B{s}", NameEn = "B", NameAr = "B" }), "categoryId");

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/crm/catalog/categories/{a}",
            new { Code = $"A{s}", NameEn = new string('x', 150), NameAr = "A" })).StatusCode);
        HttpResponseMessage taken = await admin.PutAsJsonAsync($"/api/crm/catalog/categories/{a}", new { Code = $"B{s}", NameEn = "A", NameAr = "A" });
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Contains("crm.catalog.CodeTaken", await taken.Content.ReadAsStringAsync());
    }

    /// <summary>F2-1: catalog creates get the same column limits (400) and a taken code is 409, not a 500.</summary>
    [Fact]
    public async Task F2_1_catalog_create_too_long_is_400_and_a_taken_code_is_409()
    {
        string s = Guid.NewGuid().ToString("N")[..8];
        (_, string userId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"rf21-{s}@example.com");
        HttpClient admin = CrmPortalTestSeeding.Client(fixture, userId, "crm.catalog.read", "crm.catalog.manage");
        Guid category = await IdAsync(await admin.PostAsJsonAsync("/api/crm/catalog/categories", new { Code = $"K{s}", NameEn = "K", NameAr = "K" }), "categoryId");

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/crm/catalog/categories", new { Code = new string('c', 65), NameEn = "x", NameAr = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/crm/catalog/items", new { CategoryId = category, Code = $"I{s}", NameEn = new string('n', 257), NameAr = "x" })).StatusCode);
        HttpResponseMessage takenCategory = await admin.PostAsJsonAsync("/api/crm/catalog/categories", new { Code = $"K{s}", NameEn = "again", NameAr = "again" });
        Assert.Equal(HttpStatusCode.Conflict, takenCategory.StatusCode);
        Assert.Contains("crm.catalog.CodeTaken", await takenCategory.Content.ReadAsStringAsync());
        await IdAsync(await admin.PostAsJsonAsync("/api/crm/catalog/items", new { CategoryId = category, Code = $"I{s}", NameEn = "i", NameAr = "i" }), "itemId");
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/crm/catalog/items", new { CategoryId = category, Code = $"I{s}", NameEn = "j", NameAr = "j" })).StatusCode);
        Guid attribute = await IdAsync(await admin.PostAsJsonAsync("/api/crm/catalog/attributes", new { CategoryId = category, Code = $"A{s}", NameEn = "a", NameAr = "a", DataType = 5 }), "attributeId");
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/crm/catalog/attributes", new { CategoryId = category, Code = $"A{s}", NameEn = "b", NameAr = "b", DataType = 5 })).StatusCode);
        await IdAsync(await admin.PostAsJsonAsync($"/api/crm/catalog/attributes/{attribute}/options", new { Code = $"O{s}", NameEn = "o", NameAr = "o" }), "optionId");
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/crm/catalog/attributes/{attribute}/options", new { Code = $"O{s}", NameEn = "p", NameAr = "p" })).StatusCode);
    }

    /// <summary>F-2: an inactive category hides its whole subtree from the picker, and its items can't be picked.</summary>
    [Fact]
    public async Task F2_inactive_parent_hides_the_subtree_from_picker_and_storefront()
    {
        PortalWorld w = await CrmPortalTestSeeding.CreateAsync(fixture, "rf2");
        string s = Guid.NewGuid().ToString("N")[..8];
        HttpClient o = w.Office;
        Guid type = await IdAsync(await o.PostAsJsonAsync("/api/crm/space-types", new { Code = $"T{s}", NameEn = "Bed", NameAr = "نوم" }), "spaceTypeId");
        Guid parent = await IdAsync(await o.PostAsJsonAsync("/api/crm/catalog/categories", new { Code = $"P{s}", NameEn = "Beds", NameAr = "اسرة" }), "categoryId");
        Guid child = await IdAsync(await o.PostAsJsonAsync("/api/crm/catalog/categories", new { ParentId = parent, Code = $"C{s}", NameEn = "Kids beds", NameAr = "اطفال" }), "categoryId");
        Assert.Equal(HttpStatusCode.NoContent, (await o.PostAsync($"/api/crm/catalog/categories/{parent}/space-types/{type}", null)).StatusCode);
        Guid item = await IdAsync(await o.PostAsJsonAsync("/api/crm/catalog/items", new { CategoryId = child, Code = $"I{s}", NameEn = "Bunk", NameAr = "دورين" }), "itemId");
        Guid space = await IdAsync(await o.PostAsJsonAsync($"/api/crm/projects/{w.ProjectId}/spaces", new { SpaceTypeId = type, Name = "Kids room" }), "spaceId");

        Assert.Equal(HttpStatusCode.NoContent, (await o.PutAsJsonAsync($"/api/crm/catalog/categories/{parent}/active", new { IsActive = false })).StatusCode);
        JsonElement picker = await o.GetFromJsonAsync<JsonElement>($"/api/crm/catalog/tree?spaceTypeId={type}");
        Assert.DoesNotContain(picker.EnumerateArray(), c => c.GetProperty("id").GetGuid() == child);
        Assert.Equal(HttpStatusCode.BadRequest, (await o.PostAsJsonAsync($"/api/crm/spaces/{space}/selections", new { CatalogItemId = item, Quantity = 1 })).StatusCode);
    }

    /// <summary>F-3: the 200-row cap applies after the gathering-work filter, so a task with work isn't cut off by
    /// 200 earlier tasks that have none.</summary>
    [Fact]
    public async Task F3_a_task_with_work_past_200_plain_tasks_is_still_listed()
    {
        PortalWorld w = await CrmPortalTestSeeding.CreateAsync(fixture, "rf3");
        HttpClient office = CrmPortalTestSeeding.Client(fixture, w.OfficeUserId, [.. CrmPortalTestSeeding.OfficeCodes, "stage_tasks.manage"]);
        for (int i = 0; i < 200; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await office.PostAsJsonAsync($"/api/projects/{w.ProjectId}/tasks",
                new { ProjectStageId = w.StageId, Name = $"Plain {i}", AssignedToId = w.OfficeEmployeeId })).StatusCode);
        }

        Guid working = await IdAsync(await office.PostAsJsonAsync($"/api/projects/{w.ProjectId}/tasks",
            new { ProjectStageId = w.StageId, Name = "With work", AssignedToId = w.OfficeEmployeeId }), "taskId");
        Guid itemId = await IdAsync(await office.PostAsJsonAsync($"/api/crm/periods/{w.PeriodId}/items",
            new { w.ProjectId, Category = 1, Title = "Item", Priority = 2, ProjectTaskId = working }), "itemId");
        Assert.Equal(HttpStatusCode.NoContent, (await office.PutAsync($"/api/crm/requirement-items/{itemId}/send", null)).StatusCode);

        JsonElement rows = await office.GetFromJsonAsync<JsonElement>("/api/crm/my-gathering-work");
        Assert.Contains(rows.EnumerateArray(), r => r.GetProperty("projectTaskId").GetGuid() == working);
    }

    /// <summary>F-4: a project-level meeting or closing period alone doesn't make every open task gathering work.</summary>
    [Fact]
    public async Task F4_a_task_without_crm_work_is_not_listed_just_because_the_period_closes_soon()
    {
        PortalWorld w = await CrmPortalTestSeeding.CreateAsync(fixture, "rf4"); // its open period closes in 3 days
        HttpClient office = CrmPortalTestSeeding.Client(fixture, w.OfficeUserId, [.. CrmPortalTestSeeding.OfficeCodes, "stage_tasks.manage"]);
        Guid plain = await IdAsync(await office.PostAsJsonAsync($"/api/projects/{w.ProjectId}/tasks",
            new { ProjectStageId = w.StageId, Name = "Order tiles", AssignedToId = w.OfficeEmployeeId }), "taskId");

        JsonElement rows = await office.GetFromJsonAsync<JsonElement>("/api/crm/my-gathering-work");
        Assert.DoesNotContain(rows.EnumerateArray(), r => r.GetProperty("projectTaskId").GetGuid() == plain);
    }

    /// <summary>F-5: a rejected entry re-sent without a space carries no picks (not the old space's snapshot).</summary>
    [Fact]
    public async Task F5_resent_entry_without_a_space_clears_the_snapshot()
    {
        PortalWorld w = await CrmPortalTestSeeding.CreateAsync(fixture, "rf5");
        string s = Guid.NewGuid().ToString("N")[..8];
        HttpClient o = w.Office;
        Guid type = await IdAsync(await o.PostAsJsonAsync("/api/crm/space-types", new { Code = $"T{s}", NameEn = "Kitchen", NameAr = "مطبخ" }), "spaceTypeId");
        Guid category = await IdAsync(await o.PostAsJsonAsync("/api/crm/catalog/categories", new { Code = $"C{s}", NameEn = "Cab", NameAr = "خ" }), "categoryId");
        Assert.Equal(HttpStatusCode.NoContent, (await o.PostAsync($"/api/crm/catalog/categories/{category}/space-types/{type}", null)).StatusCode);
        Guid item = await IdAsync(await o.PostAsJsonAsync("/api/crm/catalog/items", new { CategoryId = category, Code = $"I{s}", NameEn = "Unit", NameAr = "و" }), "itemId");
        Guid space = await IdAsync(await o.PostAsJsonAsync($"/api/crm/projects/{w.ProjectId}/spaces", new { SpaceTypeId = type, Name = "K" }), "spaceId");
        await IdAsync(await o.PostAsJsonAsync($"/api/crm/spaces/{space}/selections", new { CatalogItemId = item, Quantity = 1 }), "selectionId");
        Guid briefId = await IdAsync(await o.PostAsJsonAsync($"/api/crm/projects/{w.ProjectId}/brief",
            new { ProjectStageId = w.StageId, Data = new { UnitType = 1, UnitCondition = 1, Address = "F5" } }), "briefId");
        Guid entry = await IdAsync(await o.PostAsJsonAsync($"/api/crm/briefs/{briefId}/spaces", new { SpaceId = space, CountOrPieces = 1, Requirements = "x" }), "briefSpaceId");
        Assert.Equal(HttpStatusCode.NoContent, (await o.PutAsync($"/api/crm/brief-spaces/{entry}/send", null)).StatusCode);

        HttpClient portal = CrmPortalTestSeeding.Client(fixture, w.ClientUserId, "crm.portal", "crm.approvals.view_own", "crm.approvals.decide_own");
        JsonElement approvals = await portal.GetFromJsonAsync<JsonElement>("/api/crm/portal/approvals?status=pending&pageNumber=1&pageSize=50");
        Guid approvalId = approvals.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("subjectId").GetGuid() == entry).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await portal.PutAsJsonAsync($"/api/crm/portal/approvals/{approvalId}/decide", new { Decision = 3, Comment = "No space please" })).StatusCode);
        await CrmPortalTestSeeding.DrainAsync(fixture);

        HttpResponseMessage edited = await o.PutAsJsonAsync($"/api/crm/brief-spaces/{entry}",
            new { SpaceId = (Guid?)null, CountOrPieces = 1, Requirements = "general", MeetingId = (Guid?)null, ProjectTaskId = (Guid?)null });
        Assert.True(edited.StatusCode == HttpStatusCode.NoContent, await edited.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await o.PutAsync($"/api/crm/brief-spaces/{entry}/send", null)).StatusCode);

        JsonElement row = (await o.GetFromJsonAsync<JsonElement>($"/api/crm/briefs/{briefId}/spaces")).EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == entry);
        Assert.Empty(row.GetProperty("selections").EnumerateArray());
    }

    private static async Task<Guid> IdAsync(HttpResponseMessage response, string name)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty(name).GetGuid();
    }
}
