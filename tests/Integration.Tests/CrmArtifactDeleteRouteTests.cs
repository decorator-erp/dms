using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>F7: artifact link delete (the linker, or a <c>crm.artifacts.manage</c> holder; anyone else 403 after
/// the project scope's 404) and paging on the by-subject lists.</summary>
[Collection(ApiCollection.Name)]
public sealed class CrmArtifactDeleteRouteTests(ApiFixture fixture)
{
    [Fact]
    public async Task Linker_or_manager_deletes_others_403_outsider_404_and_lists_page()
    {
        PortalWorld w = await CrmPortalTestSeeding.CreateAsync(fixture, "f7");
        Guid briefId = await IdAsync(await w.Office.PostAsJsonAsync($"/api/crm/projects/{w.ProjectId}/brief",
            new { ProjectStageId = w.StageId, Data = new { UnitType = 1, UnitCondition = 1, Address = "F7" } }), "briefId");
        Guid first = await LinkAsync(w, briefId);
        Guid second = await LinkAsync(w, briefId);

        (_, string otherUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"f7-other-{Guid.NewGuid():N}@example.com");
        HttpClient readerOnly = CrmPortalTestSeeding.Client(fixture, otherUserId, "crm.artifacts.read", "project.read_all");
        HttpClient manager = CrmPortalTestSeeding.Client(fixture, otherUserId, "crm.artifacts.read", "crm.artifacts.manage", "project.read_all");
        HttpClient outsider = CrmPortalTestSeeding.Client(fixture, otherUserId, "crm.artifacts.read", "crm.artifacts.manage");
        HttpClient linker = CrmPortalTestSeeding.Client(fixture, w.OfficeUserId, "crm.artifacts.read", "project.read_all");

        Assert.Equal(HttpStatusCode.Forbidden, (await CrmPortalTestSeeding.Client(fixture, w.OfficeUserId, "project.read_all").DeleteAsync($"/api/crm/artifacts/{first}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.DeleteAsync($"/api/crm/artifacts/{first}")).StatusCode);
        HttpResponseMessage notLinker = await readerOnly.DeleteAsync($"/api/crm/artifacts/{first}");
        Assert.Equal(HttpStatusCode.Forbidden, notLinker.StatusCode);
        Assert.Contains("crm.artifacts.NotLinker", await notLinker.Content.ReadAsStringAsync());

        // Paging on the by-subject list.
        JsonElement page = await w.Office.GetFromJsonAsync<JsonElement>($"/api/crm/artifacts/by-brief/{briefId}?pageNumber=2&pageSize=1");
        Assert.Equal(second, Assert.Single(page.EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Office.GetAsync($"/api/crm/artifacts/by-brief/{briefId}?pageNumber=1&pageSize=201")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await linker.DeleteAsync($"/api/crm/artifacts/{first}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/crm/artifacts/{second}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.DeleteAsync($"/api/crm/artifacts/{second}")).StatusCode);
        Assert.Empty((await w.Office.GetFromJsonAsync<JsonElement>($"/api/crm/artifacts/by-brief/{briefId}")).EnumerateArray());
    }

    private static async Task<Guid> LinkAsync(PortalWorld w, Guid briefId)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes($"sketch {Guid.NewGuid()}"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(file, "file", "sketch.txt");
        Guid documentId = await IdAsync(await w.Office.PostAsync($"/api/projects/{w.ProjectId}/documents", content), "documentId");
        return await IdAsync(await w.Office.PostAsJsonAsync("/api/crm/artifacts", new { DocumentId = documentId, BriefId = briefId }), "artifactDocumentId");
    }

    private static async Task<Guid> IdAsync(HttpResponseMessage response, string name)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty(name).GetGuid();
    }
}
