using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// BF-1 prerequisite: Documents is scoped to the project. Before this, every Documents route checked only its
/// claim, so anyone holding <c>documents.read</c> could list and download any project's files. Now a project-bound
/// route resolves its project and asks the project guard: route claim 403 → module scope 404 → (writes) level
/// 403 <c>project_documents.InsufficientLevel</c> below Editor.
///
/// <para>One project with a stage, a task, a folder and a document, reached by each key the decorator resolves
/// from (project, stage, task, folder, document), as four callers: an outsider holding every Documents claim, a
/// Viewer member, an Editor member, and an outsider with <c>project.read_all</c>.</para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DocumentsScopeRouteTests(ApiFixture fixture)
{
    private static readonly string[] DocumentCodes =
        ["documents.read", "documents.upload", "documents.update", "documents.delete"];

    private static readonly string[] OwnerCodes =
    [
        "project.create", "project.read", "project.manage_members", "clients.create", "clients.read",
        "stages.read", "stages.manage", "scope.read", "scope.manage", "scope.confirm",
        "stage_tasks.read", "stage_tasks.manage", .. DocumentCodes,
    ];

    [Fact]
    public async Task Outsiders_get_404_on_every_key_and_members_read_by_level()
    {
        World w = await BuildAsync();

        // An outsider with every Documents claim: each key is a 404, never the project's content.
        HttpClient outsider = Client(w.OutsiderUserId, DocumentCodes);
        foreach (string route in w.ReadRoutes)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(route)).StatusCode);
        }

        HttpResponseMessage outsiderUpload = await outsider.PostAsync($"/api/projects/{w.ProjectId}/documents", File("x.txt"));
        Assert.Equal(HttpStatusCode.NotFound, outsiderUpload.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.DeleteAsync($"/api/project-documents/{w.DocumentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.PutAsJsonAsync(
            $"/api/document-folders/{w.FolderId}/rename", new { Name = "Taken" })).StatusCode);

        // A Viewer member reads every key...
        HttpClient viewer = Client(w.ViewerUserId, DocumentCodes);
        foreach (string route in w.ReadRoutes)
        {
            HttpResponseMessage read = await viewer.GetAsync(route);
            Assert.True(read.StatusCode == HttpStatusCode.OK, $"{route}: {(int)read.StatusCode} {await read.Content.ReadAsStringAsync()}");
        }

        // ...but writes nothing: 403 with the level code, not a 404 (they can see the project).
        HttpResponseMessage viewerUpload = await viewer.PostAsync($"/api/projects/{w.ProjectId}/documents", File("v.txt"));
        Assert.Equal(HttpStatusCode.Forbidden, viewerUpload.StatusCode);
        Assert.Contains("project_documents.InsufficientLevel", await viewerUpload.Content.ReadAsStringAsync());
        HttpResponseMessage viewerDelete = await viewer.DeleteAsync($"/api/project-documents/{w.DocumentId}");
        Assert.Equal(HttpStatusCode.Forbidden, viewerDelete.StatusCode);
        HttpResponseMessage viewerFolder = await viewer.PostAsJsonAsync("/api/document-folders", new { w.ProjectId, Name = "Viewer" });
        Assert.Equal(HttpStatusCode.Forbidden, viewerFolder.StatusCode);
    }

    [Fact]
    public async Task An_editor_member_writes_and_project_read_all_reaches_a_project_it_is_not_on()
    {
        World w = await BuildAsync();

        HttpClient editor = Client(w.EditorUserId, DocumentCodes);
        HttpResponseMessage upload = await editor.PostAsync($"/api/projects/{w.ProjectId}/documents", File("e.txt"));
        Assert.True(upload.StatusCode == HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        HttpResponseMessage taskUpload = await editor.PostAsync($"/api/project-tasks/{w.TaskId}/documents", File("t.txt"));
        Assert.True(taskUpload.StatusCode == HttpStatusCode.OK, await taskUpload.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await editor.PutAsJsonAsync(
            $"/api/document-folders/{w.FolderId}/rename", new { Name = "Renamed by editor" })).StatusCode);

        // project.read_all is the guard's own override — an admin or manager role reaches every project.
        HttpClient readAll = Client(w.OutsiderUserId, [.. DocumentCodes, "project.read_all"]);
        foreach (string route in w.ReadRoutes)
        {
            HttpResponseMessage read = await readAll.GetAsync(route);
            Assert.True(read.StatusCode == HttpStatusCode.OK, $"{route}: {(int)read.StatusCode}");
        }

        HttpResponseMessage readAllUpload = await readAll.PostAsync($"/api/projects/{w.ProjectId}/documents", File("r.txt"));
        Assert.True(readAllUpload.StatusCode == HttpStatusCode.OK, await readAllUpload.Content.ReadAsStringAsync());
    }

    /// <summary>A missing row and an unreachable project are the same 404 — no existence oracle.</summary>
    [Fact]
    public async Task A_missing_document_or_folder_is_the_same_404_as_an_unreachable_one()
    {
        World w = await BuildAsync();
        HttpClient readAll = Client(w.OutsiderUserId, [.. DocumentCodes, "project.read_all"]);

        Assert.Equal(HttpStatusCode.NotFound, (await readAll.GetAsync($"/api/project-documents/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await readAll.GetAsync($"/api/document-folders/{Guid.NewGuid()}/documents")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await readAll.GetAsync($"/api/project-stages/{Guid.NewGuid()}/documents")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await readAll.GetAsync($"/api/project-tasks/{Guid.NewGuid()}/documents")).StatusCode);
    }

    private sealed record World(
        Guid ProjectId, Guid StageId, Guid TaskId, Guid FolderId, Guid DocumentId,
        string ViewerUserId, string EditorUserId, string OutsiderUserId)
    {
        public string[] ReadRoutes =>
        [
            $"/api/projects/{ProjectId}/documents",
            $"/api/projects/{ProjectId}/document-folders",
            $"/api/project-stages/{StageId}/documents",
            $"/api/project-stages/{StageId}/document-folders",
            $"/api/project-tasks/{TaskId}/documents",
            $"/api/project-tasks/{TaskId}/document-folders",
            $"/api/document-folders/{FolderId}/documents",
            $"/api/document-folders/{FolderId}/children",
            $"/api/project-documents/{DocumentId}",
            $"/api/project-documents/{DocumentId}/download",
        ];
    }

    private async Task<World> BuildAsync()
    {
        string suffix = Guid.NewGuid().ToString("N")[..10];
        (_, string ownerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"ds-owner-{suffix}@example.com");
        (Guid viewerId, string viewerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"ds-viewer-{suffix}@example.com");
        (Guid editorId, string editorUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"ds-editor-{suffix}@example.com");
        (_, string outsiderUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"ds-out-{suffix}@example.com");
        HttpClient owner = Client(ownerUserId, OwnerCodes);

        (Guid projectId, Guid stageId, _) = await ProjectTestSeeding.CreateConfirmedStageProjectAsync(owner, $"DSC-{suffix}");
        HttpResponseMessage task = await owner.PostAsJsonAsync($"/api/projects/{projectId}/tasks", new { ProjectStageId = stageId, Name = "Docs task" });
        Assert.True(task.StatusCode == HttpStatusCode.OK, await task.Content.ReadAsStringAsync());
        Guid taskId = (await task.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("taskId").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync(
            $"/api/projects/{projectId}/members", new { EmployeeId = viewerId, Access = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync(
            $"/api/projects/{projectId}/members", new { EmployeeId = editorId, Access = 2 })).StatusCode);

        HttpResponseMessage folder = await owner.PostAsJsonAsync("/api/document-folders", new { ProjectId = projectId, Name = "Drawings" });
        Assert.True(folder.StatusCode == HttpStatusCode.OK, await folder.Content.ReadAsStringAsync());
        Guid folderId = (await folder.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("folderId").GetGuid();

        HttpResponseMessage upload = await owner.PostAsync($"/api/projects/{projectId}/documents", File("plan.txt"));
        Assert.True(upload.StatusCode == HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        Guid documentId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetGuid();

        return new World(projectId, stageId, taskId, folderId, documentId, viewerUserId, editorUserId, outsiderUserId);
    }

    private static MultipartFormDataContent File(string name)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes($"contents of {name}"));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        return new MultipartFormDataContent { { content, "file", name } };
    }

    private HttpClient Client(string userId, params string[] permissions)
    {
        HttpClient client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.IssueTestToken(userId, permissions));
        return client;
    }
}
