using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Identity.Contracts;
using Identity.Contracts.Auth;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// G7-08's own verify block: the app boots with all FIVE modules; <c>GET /api/permissions</c> contains
/// the <c>documents.*</c>/<c>file_templates.*</c> strings; unauthenticated → 401 and
/// authenticated-without-permission → 403 on a mutating Documents route — same shape
/// <c>ProjectsSmokeTests</c> established at G5-09, one module wider.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DocumentsSmokeTests(ApiFixture fixture)
{
    [Fact]
    public async Task GetPermissions_ContainsDocumentsModulePermissionStrings()
    {
        var client = fixture.CreateClient();
        var token = fixture.IssueTestToken(Guid.NewGuid().ToString(), IdentityPermissions.PermissionsManage);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/permissions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var modules = await response.Content.ReadFromJsonAsync<List<ModulePermissionSet>>();
        Assert.NotNull(modules);

        var documentsModule = Assert.Single(modules!, m => m.Module == "Documents");
        Assert.Contains("documents.read", documentsModule.Permissions);
        Assert.Contains("documents.upload", documentsModule.Permissions);
        Assert.Contains("documents.update", documentsModule.Permissions);
        Assert.Contains("documents.delete", documentsModule.Permissions);
        Assert.Contains("file_templates.read", documentsModule.Permissions);
        Assert.Contains("file_templates.manage", documentsModule.Permissions);
    }

    [Fact]
    public async Task CreateDocumentFolder_Unauthenticated_Returns401()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/document-folders", new
        {
            ProjectId = Guid.NewGuid(),
            Name = "Smoke",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateDocumentFolder_AuthenticatedWithoutPermission_Returns403()
    {
        var client = fixture.CreateClient();
        var token = fixture.IssueTestToken(Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/document-folders", new
        {
            ProjectId = Guid.NewGuid(),
            Name = "Smoke",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// An oversized multipart body must fail closed rather than being silently truncated or accepted —
    /// <see cref="DocumentsModule"/>'s <c>FormOptions.MultipartBodyLengthLimit</c> configured from
    /// <c>FileStorage:MaxUploadBytes</c> (G7-08). Uses a tiny configured cap so the test itself stays
    /// fast, not the real 50&nbsp;MB default.
    ///
    /// <b>400, not 413</b> — corrected from the plan's own "oversized upload → 413" text once built: a
    /// <c>MultipartBodyLengthLimit</c> violation surfaces as
    /// <see cref="Microsoft.AspNetCore.Http.BadHttpRequestException"/> while the request delegate's own
    /// form binder is reading the body (confirmed by running this test and reading the framework's own
    /// <c>StatusCode</c> off that exception, not assumed) — ASP.NET Core's own choice, 400 Bad Request,
    /// for a malformed/oversized FORM section. 413 Payload Too Large is a DIFFERENT mechanism
    /// (<c>IHttpMaxRequestBodySizeFeature.MaxRequestBodySize</c>, the whole-request-body limit Kestrel
    /// itself enforces before any form parsing starts) that this configuration does not touch. Forcing a
    /// 413 here would mean second-guessing a status the framework already assigns correctly for what it
    /// is: a bad multipart body, not an oversized HTTP request per se.
    /// </summary>
    [Fact]
    public async Task Upload_OversizedMultipartBody_Returns400()
    {
        var client = fixture.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            $"oversized-{suffix}@example.com", "P@ssw0rd123!", "Test", "User", null, "employee"));
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
        var appUserId = (await registerResponse.Content.ReadFromJsonAsync<RegisterResponseBody>())!.UserId;

        var token = fixture.IssueTestToken(appUserId, "documents.upload");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // ApiFixture configures FileStorage:MaxUploadBytes to 5 MB for this whole test collection -
        // DocumentsModule's own FormOptions.MultipartBodyLengthLimit wiring (G7-08) is what this payload
        // must trip.
        var oversized = new byte[6 * 1024 * 1024];
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(oversized);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "file", "huge.bin");

        var response = await client.PostAsync($"/api/projects/{Guid.NewGuid()}/documents", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record RegisterResponseBody(string UserId);
}
