using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>F5 (D4/D5): spaces reorder (the full id list, exactly the project's spaces, else 400) and delete
/// only when unused (409 <c>crm.spaces.InUse</c>).</summary>
[Collection(ApiCollection.Name)]
public sealed class CrmSpaceAdminRouteTests(ApiFixture fixture)
{
    [Fact]
    public async Task Reorder_exact_list_and_delete_if_unused()
    {
        PortalWorld w = await CrmPortalTestSeeding.CreateAsync(fixture, "f5");
        string s = Guid.NewGuid().ToString("N")[..8];
        HttpResponseMessage st = await w.Office.PostAsJsonAsync("/api/crm/space-types", new { Code = $"F5{s}", NameEn = "Room", NameAr = "غرفة" });
        Guid spaceType = (await st.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("spaceTypeId").GetGuid();
        Guid a = await SpaceAsync(w, spaceType, "Alpha");
        Guid b = await SpaceAsync(w, spaceType, "Bravo");
        Guid c = await SpaceAsync(w, spaceType, "Charlie");
        string order = $"/api/crm/projects/{w.ProjectId}/spaces/order";

        Assert.Equal(HttpStatusCode.Forbidden, (await CrmPortalTestSeeding.Client(fixture, w.OfficeUserId, "crm.spaces.read")
            .PutAsJsonAsync(order, new { SpaceIds = new[] { c, b, a } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Office.PutAsJsonAsync(order, new { SpaceIds = new[] { c, b } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Office.PutAsJsonAsync(order, new { SpaceIds = new[] { c, b, a, Guid.NewGuid() } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Office.PutAsJsonAsync(order, new { SpaceIds = new[] { c, c, a } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await w.Office.PutAsJsonAsync(order, new { SpaceIds = new[] { c, a, b } })).StatusCode);
        JsonElement[] spaces = [.. (await w.Office.GetFromJsonAsync<JsonElement>($"/api/crm/projects/{w.ProjectId}/spaces")).EnumerateArray()
            .OrderBy(x => x.GetProperty("sortOrder").GetInt32())];
        Assert.Equal(new[] { c, a, b }, spaces.Select(x => x.GetProperty("id").GetGuid()).ToArray());

        // A brief entry on Bravo makes it in use; Charlie is free.
        HttpResponseMessage brief = await w.Office.PostAsJsonAsync($"/api/crm/projects/{w.ProjectId}/brief",
            new { ProjectStageId = w.StageId, Data = new { UnitType = 1, UnitCondition = 1, Address = "F5" } });
        Guid briefId = (await brief.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("briefId").GetGuid();
        Assert.True((await w.Office.PostAsJsonAsync($"/api/crm/briefs/{briefId}/spaces", new { SpaceId = b, CountOrPieces = 1, Requirements = "x" })).IsSuccessStatusCode);

        HttpResponseMessage refused = await w.Office.DeleteAsync($"/api/crm/spaces/{b}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("crm.spaces.InUse", await refused.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await w.Office.DeleteAsync($"/api/crm/spaces/{c}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.Office.DeleteAsync($"/api/crm/spaces/{c}")).StatusCode);
        Assert.Equal(2, (await w.Office.GetFromJsonAsync<JsonElement>($"/api/crm/projects/{w.ProjectId}/spaces")).GetArrayLength());

        // An outsider (no project access) gets 404 from the scope check.
        (_, string outsider) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"f5-out-{s}@example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await CrmPortalTestSeeding.Client(fixture, outsider, "crm.spaces.manage").DeleteAsync($"/api/crm/spaces/{a}")).StatusCode);
    }

    private static async Task<Guid> SpaceAsync(PortalWorld w, Guid spaceType, string name)
    {
        HttpResponseMessage r = await w.Office.PostAsJsonAsync($"/api/crm/projects/{w.ProjectId}/spaces", new { SpaceTypeId = spaceType, Name = name, SortOrder = 0 });
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("spaceId").GetGuid();
    }
}
