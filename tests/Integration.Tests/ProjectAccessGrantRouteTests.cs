using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// P1-A: project access grants, end to end over HTTP. One project with two stages, a task and a document in each,
/// and a project-level document; a second project the grantee never gets. The grantee is an employee who is on no
/// project team, and most of their tokens hold <b>no</b> project, documents or sites claim at all: every route claim
/// they pass comes from the grant, read live (no new token), and every scope check counts only what the grant covers.
/// Areas are sent as their flag values (Overview 1, StagesAndTasks 2, Documents 4, Sites 8, Crm 16, Team 32); levels
/// as Viewer 1, Editor 2, Manager 3.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ProjectAccessGrantRouteTests(ApiFixture fixture)
{
    private const int Overview = 1;
    private const int StagesAndTasks = 2;
    private const int Documents = 4;
    private const int Sites = 8;

    private static readonly string[] OwnerCodes =
    [
        "project.create", "project.read", "project.manage_members", "clients.create", "clients.read",
        "stages.read", "stages.manage", "scope.read", "scope.manage", "scope.confirm",
        "stage_tasks.read", "stage_tasks.manage", "documents.read", "documents.upload", "documents.update",
        "documents.delete", "sites.read",
    ];

    [Fact]
    public async Task A_viewer_grant_on_documents_reads_that_projects_documents_and_nothing_else()
    {
        World w = await BuildAsync();
        Guid grantId = await GrantAsync(w, w.GranteeId, level: 1, areas: Documents);

        // No claim at all in the token: documents.read and project.read come from the grant.
        HttpClient grantee = Client(w.GranteeUserId);
        HttpResponseMessage list = await grantee.GetAsync($"/api/projects/{w.ProjectId}/documents");
        Assert.True(list.StatusCode == HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
        Assert.Equal(3, (await list.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await grantee.GetAsync($"/api/project-documents/{w.Stage2DocumentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await grantee.GetAsync($"/api/project-documents/{w.ProjectDocumentId}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await grantee.GetAsync($"/api/projects/{w.ProjectId}/document-folders")).StatusCode);

        // The project header opens (any grant), with MyAccess saying where access comes from.
        JsonElement project = await (await grantee.GetAsync($"/api/projects/{w.ProjectId}")).Content.ReadFromJsonAsync<JsonElement>();
        JsonElement myAccess = project.GetProperty("myAccess");
        Assert.Equal("Grant", myAccess.GetProperty("source").GetString());
        Assert.Equal(1, myAccess.GetProperty("level").GetInt32());
        Assert.Equal(Documents, myAccess.GetProperty("areas").GetInt32());
        Assert.Equal(1, project.GetProperty("myLevel").GetInt32());

        // Other areas: no implied claim (403) or no covering grant (404).
        Assert.Equal(HttpStatusCode.Forbidden, (await grantee.GetAsync($"/api/projects/{w.ProjectId}/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await grantee.GetAsync($"/api/sites?projectId={w.ProjectId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await grantee.GetAsync($"/api/projects/{w.ProjectId}/details")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await grantee.GetAsync($"/api/projects/{w.ProjectId}/members")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await grantee.PostAsync($"/api/projects/{w.ProjectId}/documents", File("x.txt"))).StatusCode);

        // Another project: 404, even with the claims in the token.
        Assert.Equal(HttpStatusCode.NotFound, (await grantee.GetAsync($"/api/projects/{w.OtherProjectId}/documents")).StatusCode);
        HttpClient withClaims = Client(w.GranteeUserId, "project.read", "documents.read", "sites.read", "stage_tasks.read");
        Assert.Equal(HttpStatusCode.NotFound, (await withClaims.GetAsync($"/api/projects/{w.OtherProjectId}/documents")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await withClaims.GetAsync($"/api/sites?projectId={w.ProjectId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await withClaims.GetAsync($"/api/projects/{w.ProjectId}/tasks")).StatusCode);

        // "Shared with me".
        JsonElement mine = Assert.Single((await (await grantee.GetAsync("/api/me/access-grants"))
            .Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
        Assert.Equal(grantId, mine.GetProperty("grantId").GetGuid());
        Assert.Equal(w.ProjectCode, mine.GetProperty("projectCode").GetString());
        Assert.Equal("Active", StatusName(mine));
    }

    /// <summary>An implied claim is user-wide; the grant is not. A member of another project whose token lacks
    /// <c>documents.read</c> must not read that project's documents because a grant elsewhere implied the code.</summary>
    [Fact]
    public async Task A_grant_implied_claim_opens_nothing_on_the_grantees_own_projects()
    {
        World w = await BuildAsync();
        Assert.Equal(HttpStatusCode.OK, (await w.Owner.PostAsJsonAsync(
            $"/api/projects/{w.OtherProjectId}/members", new { EmployeeId = w.GranteeId, Access = 3 })).StatusCode);
        HttpClient grantee = Client(w.GranteeUserId);
        Assert.Equal(HttpStatusCode.Forbidden, (await grantee.GetAsync($"/api/projects/{w.OtherProjectId}/documents")).StatusCode);

        await GrantAsync(w, w.GranteeId, level: 1, areas: Documents);

        Assert.Equal(HttpStatusCode.OK, (await grantee.GetAsync($"/api/projects/{w.ProjectId}/documents")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await grantee.GetAsync($"/api/projects/{w.OtherProjectId}/documents")).StatusCode);

        // With the claim in the token, membership counts as before.
        HttpClient withClaim = Client(w.GranteeUserId, "documents.read");
        Assert.Equal(HttpStatusCode.OK, (await withClaim.GetAsync($"/api/projects/{w.OtherProjectId}/documents")).StatusCode);
    }

    [Fact]
    public async Task A_stage_limited_grant_sees_only_that_stages_tasks_and_documents()
    {
        World w = await BuildAsync();
        await GrantAsync(w, w.GranteeId, level: 1, areas: StagesAndTasks | Documents | Overview, stageIds: [w.Stage1Id]);
        HttpClient grantee = Client(w.GranteeUserId);

        JsonElement tasks = await ReadOkAsync(grantee, $"/api/projects/{w.ProjectId}/tasks");
        Assert.Equal([w.Task1Id], tasks.EnumerateArray().Select(t => t.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.OK, (await grantee.GetAsync($"/api/projects/{w.ProjectId}/tasks/{w.Task1Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await grantee.GetAsync($"/api/projects/{w.ProjectId}/tasks/{w.Task2Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await grantee.GetAsync($"/api/projects/{w.ProjectId}/tasks?stageId={w.Stage2Id}")).StatusCode);

        JsonElement documents = await ReadOkAsync(grantee, $"/api/projects/{w.ProjectId}/documents");
        Assert.Equal([w.Stage1DocumentId], documents.EnumerateArray().Select(d => d.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.OK, (await grantee.GetAsync($"/api/project-documents/{w.Stage1DocumentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await grantee.GetAsync($"/api/project-documents/{w.Stage2DocumentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await grantee.GetAsync($"/api/project-documents/{w.ProjectDocumentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await grantee.GetAsync($"/api/project-stages/{w.Stage2Id}/documents")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await grantee.GetAsync($"/api/project-stages/{w.Stage1Id}/documents")).StatusCode);

        // The details page (Overview) shows only the granted stage, and MyAccess names it.
        JsonElement details = await ReadOkAsync(grantee, $"/api/projects/{w.ProjectId}/details");
        Assert.Equal([w.Stage1Id], details.GetProperty("stages").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()));
        Assert.Equal([w.Stage1Id], details.GetProperty("myAccess").GetProperty("stageIds").EnumerateArray().Select(s => s.GetGuid()));

        // A whole-project member sees everything, as before.
        JsonElement ownerTasks = await ReadOkAsync(w.Owner, $"/api/projects/{w.ProjectId}/tasks");
        Assert.Equal(2, ownerTasks.GetArrayLength());
    }

    [Fact]
    public async Task An_editor_grant_uploads_and_a_viewer_grant_gets_403()
    {
        World w = await BuildAsync();
        (Guid viewerId, string viewerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"pg-viewer-{w.Suffix}@example.com");
        await GrantAsync(w, w.GranteeId, level: 2, areas: Documents);
        await GrantAsync(w, viewerId, level: 1, areas: Documents);

        HttpResponseMessage editorUpload = await Client(w.GranteeUserId).PostAsync($"/api/projects/{w.ProjectId}/documents", File("e.txt"));
        Assert.True(editorUpload.StatusCode == HttpStatusCode.OK, await editorUpload.Content.ReadAsStringAsync());

        // Without the claim: the route refuses (403). With it from a role: the level refuses (403).
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(viewerUserId)
            .PostAsync($"/api/projects/{w.ProjectId}/documents", File("v.txt"))).StatusCode);
        HttpResponseMessage viewerWithClaim = await Client(viewerUserId, "documents.upload")
            .PostAsync($"/api/projects/{w.ProjectId}/documents", File("v.txt"));
        Assert.Equal(HttpStatusCode.Forbidden, viewerWithClaim.StatusCode);
        Assert.Contains("project_documents.InsufficientLevel", await viewerWithClaim.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Access_ends_at_expiry_with_no_job()
    {
        World w = await BuildAsync();
        Guid grantId = await GrantAsync(w, w.GranteeId, level: 1, areas: Documents, expiresAtUtc: DateTime.UtcNow.AddDays(1));
        HttpClient grantee = Client(w.GranteeUserId);
        HttpClient withClaim = Client(w.GranteeUserId, "documents.read");
        Assert.Equal(HttpStatusCode.OK, (await grantee.GetAsync($"/api/projects/{w.ProjectId}/documents")).StatusCode);

        // Move the window into the past, as the clock would: nothing else changes.
        await ExecuteAsync(
            """
            UPDATE projects.project_access_grants
            SET "StartsAtUtc" = now() - interval '2 days', "ExpiresAtUtc" = now() - interval '1 second'
            WHERE "Id" = @id
            """, grantId);

        Assert.Equal(HttpStatusCode.Forbidden, (await grantee.GetAsync($"/api/projects/{w.ProjectId}/documents")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await withClaim.GetAsync($"/api/projects/{w.ProjectId}/documents")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await withClaim.GetAsync($"/api/project-documents/{w.Stage1DocumentId}")).StatusCode);

        JsonElement listed = Assert.Single((await ReadOkAsync(w.Owner, $"/api/projects/{w.ProjectId}/access-grants")).EnumerateArray());
        Assert.Equal("Expired", StatusName(listed));
        HttpResponseMessage edit = await w.Owner.PutAsJsonAsync(
            $"/api/projects/{w.ProjectId}/access-grants/{grantId}", new { Level = 2, Areas = Documents, Reason = "more" });
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Empty((await ReadOkAsync(grantee, "/api/me/access-grants")).EnumerateArray());
    }

    [Fact]
    public async Task Revoke_takes_effect_on_the_next_request_with_the_same_token_and_is_final()
    {
        World w = await BuildAsync();
        Guid grantId = await GrantAsync(w, w.GranteeId, level: 1, areas: Documents | Sites);
        HttpClient grantee = Client(w.GranteeUserId);
        HttpClient withClaim = Client(w.GranteeUserId, "documents.read");
        Assert.Equal(HttpStatusCode.OK, (await grantee.GetAsync($"/api/projects/{w.ProjectId}/documents")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await withClaim.GetAsync($"/api/project-documents/{w.Stage1DocumentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await grantee.GetAsync($"/api/sites?projectId={w.ProjectId}")).StatusCode);

        HttpResponseMessage revoke = await w.Owner.PostAsJsonAsync(
            $"/api/projects/{w.ProjectId}/access-grants/{grantId}/revoke", new { Reason = "audit done" });
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await grantee.GetAsync($"/api/projects/{w.ProjectId}/documents")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await withClaim.GetAsync($"/api/project-documents/{w.Stage1DocumentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await grantee.GetAsync($"/api/sites?projectId={w.ProjectId}")).StatusCode);

        // Final: no edit, no second revoke; the list says Revoked, with who and why.
        HttpResponseMessage edit = await w.Owner.PutAsJsonAsync(
            $"/api/projects/{w.ProjectId}/access-grants/{grantId}", new { Level = 1, Areas = Documents, Reason = "again" });
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Contains("projects.access_grants.NotEditable", await edit.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await w.Owner.PostAsync(
            $"/api/projects/{w.ProjectId}/access-grants/{grantId}/revoke", content: null)).StatusCode);
        JsonElement listed = Assert.Single((await ReadOkAsync(w.Owner, $"/api/projects/{w.ProjectId}/access-grants")).EnumerateArray());
        Assert.Equal("Revoked", StatusName(listed));
        Assert.Equal("audit done", listed.GetProperty("revokeReason").GetString());

        // A new grant may be made after a revoke.
        await GrantAsync(w, w.GranteeId, level: 1, areas: Documents);
        Assert.Equal(HttpStatusCode.OK, (await grantee.GetAsync($"/api/projects/{w.ProjectId}/documents")).StatusCode);
    }

    [Fact]
    public async Task An_active_grant_is_edited_in_place_and_a_second_one_is_refused()
    {
        World w = await BuildAsync();
        Guid grantId = await GrantAsync(w, w.GranteeId, level: 1, areas: Documents);
        HttpClient grantee = Client(w.GranteeUserId);
        Assert.Equal(HttpStatusCode.Forbidden, (await grantee.GetAsync($"/api/projects/{w.ProjectId}/tasks")).StatusCode);

        HttpResponseMessage second = await w.Owner.PostAsJsonAsync($"/api/projects/{w.ProjectId}/access-grants", new
        {
            EmployeeId = w.GranteeId,
            Level = 1,
            Areas = Sites,
            Reason = "another",
        });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("projects.access_grants.AlreadyGranted", await second.Content.ReadAsStringAsync());

        HttpResponseMessage edit = await w.Owner.PutAsJsonAsync($"/api/projects/{w.ProjectId}/access-grants/{grantId}", new
        {
            Level = 1,
            Areas = Documents | StagesAndTasks,
            StageIds = new[] { w.Stage2Id },
            Reason = "widened",
        });
        Assert.Equal(HttpStatusCode.NoContent, edit.StatusCode);

        JsonElement tasks = await ReadOkAsync(grantee, $"/api/projects/{w.ProjectId}/tasks");
        Assert.Equal([w.Task2Id], tasks.EnumerateArray().Select(t => t.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task Validation_names_the_rule()
    {
        World w = await BuildAsync();

        async Task AssertRefusedAsync(object body, HttpStatusCode status, string code)
        {
            HttpResponseMessage response = await w.Owner.PostAsJsonAsync($"/api/projects/{w.ProjectId}/access-grants", body);
            string text = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == status, $"{(int)response.StatusCode} {text}");
            Assert.Contains(code, text);
        }

        await AssertRefusedAsync(
            new { EmployeeId = w.GranteeId, Level = 1, Areas = Documents, StageIds = new[] { w.OtherStageId }, Reason = "r" },
            HttpStatusCode.BadRequest, "projects.access_grants.StageNotInProject");
        await AssertRefusedAsync(
            new
            {
                EmployeeId = w.GranteeId,
                Level = 1,
                Areas = Documents,
                Reason = "r",
                StartsAtUtc = DateTime.UtcNow.AddDays(2),
                ExpiresAtUtc = DateTime.UtcNow.AddDays(1),
            },
            HttpStatusCode.BadRequest, "projects.access_grants.InvalidWindow");
        await AssertRefusedAsync(
            new { EmployeeId = w.OwnerEmployeeId, Level = 1, Areas = Documents, Reason = "r" },
            HttpStatusCode.BadRequest, "projects.access_grants.SelfGrant");
        await AssertRefusedAsync(
            new { EmployeeId = Guid.NewGuid(), Level = 1, Areas = Documents, Reason = "r" },
            HttpStatusCode.NotFound, "projects.access_grants.EmployeeNotFound");
        await AssertRefusedAsync(
            new { EmployeeId = w.GranteeId, Level = 1, Areas = Documents, Reason = "" },
            HttpStatusCode.BadRequest, "Reason");
    }

    [Fact]
    public async Task A_manager_by_membership_grants_without_manage_access_and_others_cannot()
    {
        World w = await BuildAsync();
        (Guid managerId, string managerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"pg-mgr-{w.Suffix}@example.com");
        (Guid editorId, string editorUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"pg-ed-{w.Suffix}@example.com");
        (_, string outsiderUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"pg-out-{w.Suffix}@example.com");
        Assert.Equal(HttpStatusCode.OK, (await w.Owner.PostAsJsonAsync(
            $"/api/projects/{w.ProjectId}/members", new { EmployeeId = managerId, Access = 3 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.Owner.PostAsJsonAsync(
            $"/api/projects/{w.ProjectId}/members", new { EmployeeId = editorId, Access = 2 })).StatusCode);

        object grant = new { EmployeeId = w.GranteeId, Level = 3, Areas = Documents, Reason = "manager grants" };
        string route = $"/api/projects/{w.ProjectId}/access-grants";

        // Route claim first.
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(managerUserId).PostAsJsonAsync(route, grant)).StatusCode);

        // An outsider: 404. An Editor member: 403 (they can see the project). Neither lists grants.
        Assert.Equal(HttpStatusCode.NotFound, (await Client(outsiderUserId, "project.read").PostAsJsonAsync(route, grant)).StatusCode);
        HttpResponseMessage editor = await Client(editorUserId, "project.read").PostAsJsonAsync(route, grant);
        Assert.Equal(HttpStatusCode.Forbidden, editor.StatusCode);
        Assert.Contains("projects.access_grants.NotAllowed", await editor.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(editorUserId, "project.read").GetAsync(route)).StatusCode);

        // A Manager member: up to Manager, never to someone already on the project.
        HttpClient manager = Client(managerUserId, "project.read");
        HttpResponseMessage created = await manager.PostAsJsonAsync(route, grant);
        Assert.True(created.StatusCode == HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        HttpResponseMessage member = await manager.PostAsJsonAsync(
            route, new { EmployeeId = editorId, Level = 1, Areas = Documents, Reason = "already on it" });
        Assert.Equal(HttpStatusCode.Conflict, member.StatusCode);
        Assert.Contains("projects.access_grants.AlreadyMember", await member.Content.ReadAsStringAsync());

        // project.read_all alone does not manage grants; project.manage_access does.
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(outsiderUserId, "project.read", "project.read_all").GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client(outsiderUserId, "project.read", "project.read_all", "project.manage_access").GetAsync(route)).StatusCode);

        // A grantee, even a Manager one, never manages grants.
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(w.GranteeUserId, "project.read").GetAsync(route)).StatusCode);
    }

    [Fact]
    public async Task The_projects_list_includes_the_granted_project_with_its_access()
    {
        World w = await BuildAsync();
        DateTime expires = DateTime.UtcNow.AddDays(7);
        await GrantAsync(w, w.GranteeId, level: 2, areas: Documents | Overview, expiresAtUtc: expires);

        JsonElement page = await ReadOkAsync(Client(w.GranteeUserId), "/api/projects?pageNumber=1&pageSize=50");
        JsonElement item = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(w.ProjectId, item.GetProperty("id").GetGuid());
        Assert.Equal(2, item.GetProperty("myLevel").GetInt32());
        JsonElement access = item.GetProperty("myAccess");
        Assert.Equal("Grant", access.GetProperty("source").GetString());
        Assert.Equal(Documents | Overview, access.GetProperty("areas").GetInt32());
        Assert.Equal(expires, access.GetProperty("expiresAtUtc").GetDateTime().ToUniversalTime(), TimeSpan.FromMilliseconds(1));

        // mine=true is membership only.
        JsonElement mine = await ReadOkAsync(Client(w.GranteeUserId), "/api/projects?pageNumber=1&pageSize=50&mine=true");
        Assert.Empty(mine.GetProperty("items").EnumerateArray());

        // Members and read_all are unchanged: Source says so.
        JsonElement owner = await ReadOkAsync(w.Owner, $"/api/projects/{w.ProjectId}");
        Assert.Equal("Member", owner.GetProperty("myAccess").GetProperty("source").GetString());
        Assert.Equal(3, owner.GetProperty("myLevel").GetInt32());
        JsonElement readAll = await ReadOkAsync(Client(w.GranteeUserId, "project.read", "project.read_all"), $"/api/projects/{w.OtherProjectId}");
        Assert.Equal("ReadAll", readAll.GetProperty("myAccess").GetProperty("source").GetString());
    }

    private sealed record World(
        string Suffix, HttpClient Owner, Guid OwnerEmployeeId, string ProjectCode, Guid ProjectId, Guid Stage1Id,
        Guid Stage2Id, Guid Task1Id, Guid Task2Id, Guid Stage1DocumentId, Guid Stage2DocumentId, Guid ProjectDocumentId,
        Guid OtherProjectId, Guid OtherStageId, Guid GranteeId, string GranteeUserId);

    private async Task<World> BuildAsync()
    {
        string suffix = Guid.NewGuid().ToString("N")[..10];
        (Guid ownerId, string ownerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"pg-owner-{suffix}@example.com");
        (Guid granteeId, string granteeUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"pg-grantee-{suffix}@example.com");
        HttpClient owner = Client(ownerUserId, OwnerCodes);

        string code = $"PG-{suffix}";
        (Guid projectId, Guid stage1Id, _) = await ProjectTestSeeding.CreateConfirmedStageProjectAsync(owner, code);
        HttpResponseMessage stage2 = await owner.PostAsJsonAsync($"/api/projects/{projectId}/stages", new { Name = "Build", Order = 2 });
        Assert.True(stage2.StatusCode == HttpStatusCode.OK, await stage2.Content.ReadAsStringAsync());
        Guid stage2Id = (await stage2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("stageId").GetGuid();

        Guid task1Id = await CreateTaskAsync(owner, projectId, stage1Id, "Stage 1 task");
        Guid task2Id = await CreateTaskAsync(owner, projectId, stage2Id, "Stage 2 task");
        Guid stage1Doc = await UploadAsync(owner, $"/api/project-stages/{stage1Id}/documents", "s1.txt");
        Guid stage2Doc = await UploadAsync(owner, $"/api/project-stages/{stage2Id}/documents", "s2.txt");
        Guid projectDoc = await UploadAsync(owner, $"/api/projects/{projectId}/documents", "p.txt");

        (Guid otherProjectId, Guid otherStageId, _) = await ProjectTestSeeding.CreateConfirmedStageProjectAsync(owner, $"PGO-{suffix}");
        await UploadAsync(owner, $"/api/projects/{otherProjectId}/documents", "o.txt");

        return new World(
            suffix, owner, ownerId, code, projectId, stage1Id, stage2Id, task1Id, task2Id, stage1Doc, stage2Doc, projectDoc,
            otherProjectId, otherStageId, granteeId, granteeUserId);
    }

    private static async Task<Guid> GrantAsync(
        World w, Guid employeeId, int level, int areas, Guid[]? stageIds = null, DateTime? expiresAtUtc = null)
    {
        HttpResponseMessage response = await w.Owner.PostAsJsonAsync($"/api/projects/{w.ProjectId}/access-grants", new
        {
            EmployeeId = employeeId,
            Level = level,
            Areas = areas,
            StageIds = stageIds ?? [],
            ExpiresAtUtc = expiresAtUtc,
            Reason = "Q3 audit",
        });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("grantId").GetGuid();
    }

    private static async Task<Guid> CreateTaskAsync(HttpClient owner, Guid projectId, Guid stageId, string name)
    {
        HttpResponseMessage task = await owner.PostAsJsonAsync($"/api/projects/{projectId}/tasks", new { ProjectStageId = stageId, Name = name });
        Assert.True(task.StatusCode == HttpStatusCode.OK, await task.Content.ReadAsStringAsync());
        return (await task.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("taskId").GetGuid();
    }

    private static async Task<Guid> UploadAsync(HttpClient owner, string route, string name)
    {
        HttpResponseMessage upload = await owner.PostAsync(route, File(name));
        Assert.True(upload.StatusCode == HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        return (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetGuid();
    }

    private static async Task<JsonElement> ReadOkAsync(HttpClient client, string route)
    {
        HttpResponseMessage response = await client.GetAsync(route);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{route}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>The status as its name, whether the API writes enums as numbers or strings.</summary>
    private static string StatusName(JsonElement row)
    {
        JsonElement status = row.GetProperty("status");
        return status.ValueKind == JsonValueKind.Number
            ? status.GetInt32() switch { 1 => "Upcoming", 2 => "Active", 3 => "Expired", 4 => "Revoked", _ => "?" }
            : status.GetString()!;
    }

    private async Task ExecuteAsync(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
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
