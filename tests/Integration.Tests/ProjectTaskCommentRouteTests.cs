using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>E6: comment edit and delete. The author may edit or delete their own comment; a
/// <c>stage_tasks.manage</c> holder may edit or delete anyone's; another member is 403 after the scope check
/// (an outsider is 404). No notification references a comment, so delete is a hard delete.</summary>
[Collection(ApiCollection.Name)]
public sealed class ProjectTaskCommentRouteTests(ApiFixture fixture)
{
    [Fact]
    public async Task Author_edits_and_deletes_manager_may_too_other_member_403_outsider_404()
    {
        string suffix = Guid.NewGuid().ToString("N")[..10];
        (_, string ownerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"cm-owner-{suffix}@example.com");
        (Guid authorId, string authorUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"cm-author-{suffix}@example.com");
        (Guid otherId, string otherUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"cm-other-{suffix}@example.com");
        (_, string outsiderUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"cm-out-{suffix}@example.com");
        HttpClient owner = Client(ownerUserId,
            "project.create", "project.read", "clients.create", "clients.read", "stage_tasks.read", "stage_tasks.manage", "project.manage_members");

        Guid projectId = await ProjectTestSeeding.CreateFreeStyleProjectAsync(owner, $"CM-{suffix}");
        foreach (Guid employee in new[] { authorId, otherId })
        {
            Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/projects/{projectId}/members", new { EmployeeId = employee, Access = 2 })).StatusCode);
        }

        HttpResponseMessage task = await owner.PostAsJsonAsync($"/api/projects/{projectId}/tasks", new { Name = "Discuss" });
        Guid taskId = (await task.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("taskId").GetGuid();
        string comments = $"/api/projects/{projectId}/tasks/{taskId}/comments";

        // The author writes with the existing route (stage_tasks.manage), then edits with only read.
        HttpClient authorWriter = Client(authorUserId, "stage_tasks.read", "stage_tasks.manage");
        HttpResponseMessage posted = await authorWriter.PostAsJsonAsync(comments, new { Content = "First draft" });
        Guid commentId = (await posted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("commentId").GetGuid();
        HttpClient author = Client(authorUserId, "stage_tasks.read");
        HttpClient other = Client(otherUserId, "stage_tasks.read");

        Assert.Equal(HttpStatusCode.Forbidden, (await Client(authorUserId).PutAsJsonAsync($"{comments}/{commentId}", new { Content = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client(outsiderUserId, "stage_tasks.read").PutAsJsonAsync($"{comments}/{commentId}", new { Content = "x" })).StatusCode);
        HttpResponseMessage notMine = await other.PutAsJsonAsync($"{comments}/{commentId}", new { Content = "hijack" });
        Assert.Equal(HttpStatusCode.Forbidden, notMine.StatusCode);
        Assert.Contains("projects.comments.NotAuthor", await notMine.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await other.DeleteAsync($"{comments}/{commentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PutAsJsonAsync($"{comments}/{commentId}", new { Content = "" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsJsonAsync($"{comments}/{commentId}", new { Content = "Final" })).StatusCode);
        JsonElement row = Assert.Single((await owner.GetFromJsonAsync<JsonElement>(comments)).EnumerateArray());
        Assert.Equal("Final", row.GetProperty("content").GetString());
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("updatedAtUtc").ValueKind);

        // A manager edits someone else's; the author deletes.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"{comments}/{commentId}", new { Content = "Moderated" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.DeleteAsync($"{comments}/{commentId}")).StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement>(comments)).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await author.DeleteAsync($"{comments}/{commentId}")).StatusCode);
    }

    private HttpClient Client(string userId, params string[] permissions)
    {
        HttpClient client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.IssueTestToken(userId, permissions));
        return client;
    }
}
