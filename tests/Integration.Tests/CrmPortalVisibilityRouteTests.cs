using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// P2-A, client portal visibility. The office switches portal sections on per project and shares single items
/// (documents, images, site photos, stages, tasks); the client sees an item only while its section is on AND it
/// is shared. Files reach the client only through <c>/api/crm/portal/projects/{p}/files/{shareId}</c>; no raw
/// document id is ever in a client response.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CrmPortalVisibilityRouteTests(ApiFixture fixture)
{
    private static readonly string[] ExtraOfficeCodes =
    [
        "crm.portal.manage", "documents.delete", "sites.read", "sites.create", "sites.update", "stage_tasks.read",
        "stage_tasks.manage",
    ];

    private static readonly string[] PortalCodes = ["crm.portal", "crm.portal.files.view_own", "crm.portal.progress.view_own"];

    [Fact]
    public async Task A_shared_pdf_streams_as_an_attachment_and_a_shared_image_inline_only_while_the_section_is_on()
    {
        (PortalWorld w, HttpClient portal) = await WorldAsync("pv-stream");
        Guid pdf = await UploadAsync(w, "plan.pdf", "application/pdf", "%PDF-1.4 plan");
        Guid png = await UploadAsync(w, "kitchen.png", "image/png", "PNG kitchen bytes");
        Guid unsharedDocument = await UploadAsync(w, "internal.pdf", "application/pdf", "%PDF internal");

        Guid pdfShare = await ShareAsync(w, kind: 1, pdf, title: "Floor plan", note: "Latest layout");
        Guid pngShare = await ShareAsync(w, kind: 2, png, title: "Kitchen");

        // Every section is off by default: nothing is reachable yet.
        Assert.Equal(HttpStatusCode.NotFound, (await portal.GetAsync(Files(w))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await portal.GetAsync(Gallery(w))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await portal.GetAsync(File(w, pdfShare))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await portal.GetAsync(File(w, pngShare))).StatusCode);

        await SettingsAsync(w, documents: true, gallery: true);

        JsonElement files = await portal.GetFromJsonAsync<JsonElement>(Files(w));
        JsonElement listed = Assert.Single(files.EnumerateArray());
        Assert.Equal(pdfShare, listed.GetProperty("shareId").GetGuid());
        Assert.Equal("Floor plan", listed.GetProperty("title").GetString());
        Assert.Equal("Latest layout", listed.GetProperty("note").GetString());
        Assert.Equal("application/pdf", listed.GetProperty("contentType").GetString());
        Assert.False(listed.GetProperty("isImage").GetBoolean());
        string raw = await (await portal.GetAsync(Files(w))).Content.ReadAsStringAsync();
        Assert.DoesNotContain(pdf.ToString(), raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("documentId", raw, StringComparison.OrdinalIgnoreCase);

        HttpResponseMessage pdfStream = await portal.GetAsync(File(w, pdfShare));
        Assert.Equal(HttpStatusCode.OK, pdfStream.StatusCode);
        Assert.Equal("application/pdf", pdfStream.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", pdfStream.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("%PDF-1.4 plan", await pdfStream.Content.ReadAsStringAsync());

        JsonElement gallery = await portal.GetFromJsonAsync<JsonElement>(Gallery(w));
        JsonElement image = Assert.Single(gallery.EnumerateArray());
        Assert.Equal(pngShare, image.GetProperty("shareId").GetGuid());
        Assert.True(image.GetProperty("isImage").GetBoolean());

        HttpResponseMessage pngStream = await portal.GetAsync(File(w, pngShare));
        Assert.Equal(HttpStatusCode.OK, pngStream.StatusCode);
        Assert.Equal("image/png", pngStream.Content.Headers.ContentType!.MediaType);
        Assert.Equal("inline", pngStream.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("PNG kitchen bytes", await pngStream.Content.ReadAsStringAsync());

        // An unshared item has no share id; neither its document id nor an unknown id reaches anything.
        Assert.Equal(HttpStatusCode.NotFound, (await portal.GetAsync(File(w, unsharedDocument))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await portal.GetAsync(File(w, Guid.NewGuid()))).StatusCode);

        // A PDF is not an image, an item of another project is not found, and a second active share is a conflict.
        Assert.Equal(HttpStatusCode.BadRequest, (await PostShareAsync(w, kind: 2, pdf)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostShareAsync(w, kind: 1, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostShareAsync(w, kind: 1, pdf)).StatusCode);

        // The client never holds the files code: 403 before anything else.
        HttpClient noFiles = CrmPortalTestSeeding.Client(fixture, w.ClientUserId, "crm.portal", "crm.portal.progress.view_own");
        Assert.Equal(HttpStatusCode.Forbidden, (await noFiles.GetAsync(File(w, pdfShare))).StatusCode);
    }

    [Fact]
    public async Task A_section_switched_off_an_unshare_and_a_deleted_source_each_hide_the_item_at_once()
    {
        (PortalWorld w, HttpClient portal) = await WorldAsync("pv-hide");
        Guid pdf = await UploadAsync(w, "contract.pdf", "application/pdf", "%PDF contract");
        Guid other = await UploadAsync(w, "spec.pdf", "application/pdf", "%PDF spec");
        Guid share = await ShareAsync(w, kind: 1, pdf);
        Guid otherShare = await ShareAsync(w, kind: 1, other);
        await SettingsAsync(w, documents: true);
        Assert.Equal(HttpStatusCode.OK, (await portal.GetAsync(File(w, share))).StatusCode);

        // Section off: the list and the item are 404; back on, both return.
        await SettingsAsync(w, documents: false);
        Assert.Equal(HttpStatusCode.NotFound, (await portal.GetAsync(Files(w))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await portal.GetAsync(File(w, share))).StatusCode);
        await SettingsAsync(w, documents: true);
        Assert.Equal(HttpStatusCode.OK, (await portal.GetAsync(File(w, share))).StatusCode);

        // Unshare takes effect on the next request.
        Assert.Equal(HttpStatusCode.NoContent, (await w.Office.DeleteAsync($"{Office(w)}/shares/{share}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await portal.GetAsync(File(w, share))).StatusCode);
        Assert.DoesNotContain(share, ShareIds(await portal.GetFromJsonAsync<JsonElement>(Files(w))));
        Assert.Equal(HttpStatusCode.NotFound, (await w.Office.DeleteAsync($"{Office(w)}/shares/{share}")).StatusCode);

        // The source document deleted: gone from the list, 404 on the stream, flagged unavailable for the office.
        Assert.Contains(otherShare, ShareIds(await portal.GetFromJsonAsync<JsonElement>(Files(w))));
        Assert.Equal(HttpStatusCode.NoContent, (await w.Office.DeleteAsync($"/api/project-documents/{other}")).StatusCode);
        Assert.DoesNotContain(otherShare, ShareIds(await portal.GetFromJsonAsync<JsonElement>(Files(w))));
        Assert.Equal(HttpStatusCode.NotFound, (await portal.GetAsync(File(w, otherShare))).StatusCode);
        JsonElement officeShares = await w.Office.GetFromJsonAsync<JsonElement>($"{Office(w)}/shares");
        JsonElement flagged = officeShares.EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == otherShare);
        Assert.False(flagged.GetProperty("isAvailable").GetBoolean());
    }

    [Fact]
    public async Task Follow_latest_serves_the_newest_revision_and_a_pinned_share_keeps_its_version()
    {
        (PortalWorld w, HttpClient portal) = await WorldAsync("pv-latest");
        Guid followed = await UploadAsync(w, "layout-v1.pdf", "application/pdf", "layout version one");
        Guid pinned = await UploadAsync(w, "quote-v1.pdf", "application/pdf", "quote version one");
        Guid followShare = await ShareAsync(w, kind: 1, followed);
        Guid pinnedShare = await ShareAsync(w, kind: 1, pinned, followLatest: false);
        await SettingsAsync(w, documents: true);

        await ReviseAsync(followed, w, "layout-v2.pdf", "layout version two");
        await ReviseAsync(pinned, w, "quote-v2.pdf", "quote version two");

        HttpResponseMessage latest = await portal.GetAsync(File(w, followShare));
        Assert.Equal("layout version two", await latest.Content.ReadAsStringAsync());
        Assert.Equal("layout-v2.pdf", latest.Content.Headers.ContentDisposition!.FileNameStar ?? latest.Content.Headers.ContentDisposition.FileName?.Trim('"'));
        Assert.Equal("quote version one", await (await portal.GetAsync(File(w, pinnedShare))).Content.ReadAsStringAsync());

        JsonElement listed = (await portal.GetFromJsonAsync<JsonElement>(Files(w))).EnumerateArray()
            .Single(f => f.GetProperty("shareId").GetGuid() == followShare);
        Assert.Equal("layout-v2.pdf", listed.GetProperty("fileName").GetString());

        // Turning follow-latest on for the pinned share moves it to the newest revision.
        Assert.Equal(HttpStatusCode.NoContent, (await w.Office.PutAsJsonAsync($"{Office(w)}/shares/{pinnedShare}",
            new { ClientTitle = "Quote", ClientNote = (string?)null, FollowLatest = true })).StatusCode);
        Assert.Equal("quote version two", await (await portal.GetAsync(File(w, pinnedShare))).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Another_clients_project_an_outsider_employee_and_a_missing_manage_code_are_refused()
    {
        (PortalWorld a, HttpClient portalA) = await WorldAsync("pv-own-a");
        PortalWorld b = await CrmPortalTestSeeding.CreateAsync(fixture, "pv-own-b", a.Office, a.OfficeEmployeeId, a.OfficeUserId);
        Guid bDocument = await UploadAsync(b, "b.pdf", "application/pdf", "%PDF b");
        Guid bShare = await ShareAsync(b, kind: 1, bDocument);
        await SettingsAsync(b, progress: true, documents: true, gallery: true, tasks: true, updates: true);

        foreach (string route in new[] { Overview(b), Files(b), Gallery(b), Tasks(b), Updates(b), File(b, bShare) })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await portalA.GetAsync(route)).StatusCode);
        }

        // The project's own client sees it.
        HttpClient portalB = CrmPortalTestSeeding.Client(fixture, b.ClientUserId, PortalCodes);
        Assert.Equal(HttpStatusCode.OK, (await portalB.GetAsync(File(b, bShare))).StatusCode);

        // An employee holding the manage code but not on the project: every management route is 404.
        string suffix = Guid.NewGuid().ToString("N")[..8];
        (_, string outsiderUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"pv-outsider-{suffix}@example.com");
        HttpClient outsider = CrmPortalTestSeeding.Client(fixture, outsiderUserId, "crm.portal.manage", "clients.read");
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"{Office(b)}/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.PutAsJsonAsync($"{Office(b)}/settings", SettingsBody())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"{Office(b)}/shares")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.PostAsJsonAsync($"{Office(b)}/shares", new { ItemKind = 1, ItemId = bDocument })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.DeleteAsync($"{Office(b)}/shares/{bShare}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"{Office(b)}/updates")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"{Office(b)}/preview")).StatusCode);

        // An employee without crm.portal.manage: every write is 403, even with project.read_all.
        (_, string readerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"pv-reader-{suffix}@example.com");
        HttpClient reader = CrmPortalTestSeeding.Client(fixture, readerUserId, "clients.read", "project.read_all");
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"{Office(b)}/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PutAsJsonAsync($"{Office(b)}/settings", SettingsBody())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync($"{Office(b)}/shares", new { ItemKind = 1, ItemId = bDocument })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.DeleteAsync($"{Office(b)}/shares/{bShare}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync($"{Office(b)}/updates", new { Title = "x", Body = "y" })).StatusCode);

        // A client is never an office manager.
        Assert.Equal(HttpStatusCode.Forbidden, (await portalB.GetAsync($"{Office(b)}/settings")).StatusCode);
    }

    [Fact]
    public async Task The_office_preview_equals_what_the_client_sees()
    {
        (PortalWorld w, HttpClient portal) = await WorldAsync("pv-preview");
        Guid pdf = await UploadAsync(w, "brief.pdf", "application/pdf", "%PDF brief");
        Guid png = await UploadAsync(w, "living.png", "image/png", "PNG living");
        Guid taskId = await CreateTaskAsync(w, "Order the tiles");
        Guid pdfShare = await ShareAsync(w, kind: 1, pdf);
        Guid pngShare = await ShareAsync(w, kind: 2, png, title: "Living room");
        Guid taskShare = await ShareAsync(w, kind: 5, taskId, title: "Tiles ordered", note: "Arriving next week");
        await PublishUpdateAsync(w, "Kitchen tiles installed", "All tiles are in.", [pngShare], notify: false);
        Guid draft = await CreateUpdateAsync(w, "Draft only", "Not yet.", []);

        // Progress off: no progress block. Progress on without a shared stage: every stage.
        await SettingsAsync(w, documents: true, gallery: true, tasks: true, updates: true);
        JsonElement overview = await portal.GetFromJsonAsync<JsonElement>(Overview(w));
        Assert.Equal(JsonValueKind.Null, overview.GetProperty("progress").ValueKind);
        Assert.False(overview.GetProperty("sections").GetProperty("progress").GetBoolean());
        Assert.Equal(1, overview.GetProperty("counts").GetProperty("documents").GetInt32());
        Assert.Equal(1, overview.GetProperty("counts").GetProperty("images").GetInt32());
        Assert.Equal(1, overview.GetProperty("counts").GetProperty("tasks").GetInt32());
        Assert.Equal(1, overview.GetProperty("counts").GetProperty("updates").GetInt32());

        await SettingsAsync(w, progress: true, documents: true, gallery: true, tasks: true, updates: true);
        overview = await portal.GetFromJsonAsync<JsonElement>(Overview(w));
        JsonElement progress = overview.GetProperty("progress");
        Assert.False(progress.GetProperty("limitedToSharedStages").GetBoolean());
        Assert.Equal("Design", Assert.Single(progress.GetProperty("stages").EnumerateArray()).GetProperty("name").GetString());

        // A shared stage limits the timeline and carries its client title.
        await ShareAsync(w, kind: 4, w.StageId, title: "Design phase");
        overview = await portal.GetFromJsonAsync<JsonElement>(Overview(w));
        progress = overview.GetProperty("progress");
        Assert.True(progress.GetProperty("limitedToSharedStages").GetBoolean());
        Assert.Equal("Design phase", Assert.Single(progress.GetProperty("stages").EnumerateArray()).GetProperty("name").GetString());

        JsonElement tasks = await portal.GetFromJsonAsync<JsonElement>(Tasks(w));
        JsonElement task = Assert.Single(tasks.EnumerateArray());
        Assert.Equal(taskShare, task.GetProperty("shareId").GetGuid());
        Assert.Equal("Tiles ordered", task.GetProperty("title").GetString());
        Assert.Equal("Design", task.GetProperty("stageName").GetString());

        JsonElement updates = await portal.GetFromJsonAsync<JsonElement>(Updates(w));
        JsonElement update = Assert.Single(updates.EnumerateArray());
        Assert.NotEqual(draft, update.GetProperty("id").GetGuid());
        Assert.Equal(pngShare, Assert.Single(update.GetProperty("attachments").EnumerateArray()).GetProperty("shareId").GetGuid());

        JsonElement preview = await w.Office.GetFromJsonAsync<JsonElement>($"{Office(w)}/preview");
        Assert.Equal(overview.GetRawText(), preview.GetProperty("overview").GetRawText());
        Assert.Equal((await portal.GetFromJsonAsync<JsonElement>(Files(w))).GetRawText(), preview.GetProperty("files").GetRawText());
        Assert.Equal((await portal.GetFromJsonAsync<JsonElement>(Gallery(w))).GetRawText(), preview.GetProperty("gallery").GetRawText());
        Assert.Equal(tasks.GetRawText(), preview.GetProperty("tasks").GetRawText());
        Assert.Equal(updates.GetRawText(), preview.GetProperty("updates").GetRawText());
        Assert.Contains(pdfShare, ShareIds(preview.GetProperty("files")));

        // Reordering puts the image ahead in the office list.
        Assert.Equal(HttpStatusCode.NoContent, (await w.Office.PutAsJsonAsync($"{Office(w)}/shares/order",
            new { ShareIds = new[] { pngShare, pdfShare } })).StatusCode);
        JsonElement ordered = await w.Office.GetFromJsonAsync<JsonElement>($"{Office(w)}/shares");
        Guid[] orderedIds = [.. ShareIds(ordered, "id")];
        Assert.True(Array.IndexOf(orderedIds, pngShare) < Array.IndexOf(orderedIds, pdfShare));
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Office.PutAsJsonAsync($"{Office(w)}/shares/order",
            new { ShareIds = new[] { pngShare, Guid.NewGuid() } })).StatusCode);
    }

    [Fact]
    public async Task A_shared_site_photo_shows_in_the_gallery_while_site_surveys_is_on()
    {
        (PortalWorld w, HttpClient portal) = await WorldAsync("pv-site");
        Guid png = await UploadAsync(w, "wall.png", "image/png", "PNG wall");
        HttpResponseMessage site = await w.Office.PostAsJsonAsync("/api/sites", new { w.ProjectId, Data = new { Address = "1 Site Rd" } });
        Assert.True(site.StatusCode == HttpStatusCode.Created, await site.Content.ReadAsStringAsync());
        Guid siteId = (await site.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("siteId").GetGuid();
        HttpResponseMessage link = await w.Office.PostAsJsonAsync($"/api/sites/{siteId}/documents", new { DocumentId = png, Kind = 3 });
        Assert.True(link.StatusCode == HttpStatusCode.Created, await link.Content.ReadAsStringAsync());
        Guid linkId = (await link.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentLinkId").GetGuid();

        Guid share = await ShareAsync(w, kind: 3, linkId, title: "Wall before");
        await SettingsAsync(w, gallery: true);
        Assert.DoesNotContain(share, ShareIds(await portal.GetFromJsonAsync<JsonElement>(Gallery(w))));
        Assert.Equal(HttpStatusCode.NotFound, (await portal.GetAsync(File(w, share))).StatusCode);

        await SettingsAsync(w, siteSurveys: true);
        JsonElement photo = Assert.Single((await portal.GetFromJsonAsync<JsonElement>(Gallery(w))).EnumerateArray());
        Assert.Equal(share, photo.GetProperty("shareId").GetGuid());
        Assert.Equal(3, photo.GetProperty("kind").GetInt32());
        HttpResponseMessage stream = await portal.GetAsync(File(w, share));
        Assert.Equal("inline", stream.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("PNG wall", await stream.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Publishing_with_notify_client_notifies_the_client_once_even_after_an_outbox_redelivery()
    {
        (PortalWorld w, HttpClient portal) = await WorldAsync("pv-notify");
        await SettingsAsync(w, documents: true, updates: true);

        Guid update = await CreateUpdateAsync(w, "Site cleared", "Ready for works.", []);
        Assert.Empty((await portal.GetFromJsonAsync<JsonElement>(Updates(w))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, (await w.Office.PostAsJsonAsync($"{Office(w)}/updates/{update}/publish", new { NotifyClient = true })).StatusCode);
        await CrmPortalTestSeeding.DrainAsync(fixture);
        Assert.Equal(1, await CountNotificationsAsync(w.ClientUserId, type: 541));
        Assert.Single((await portal.GetFromJsonAsync<JsonElement>(Updates(w))).EnumerateArray());

        // The notification row committed but the inbox row did not: the outbox redelivers the event.
        await RedeliverAsync(update);
        await CrmPortalTestSeeding.DrainAsync(fixture);
        Assert.Equal(1, await CountNotificationsAsync(w.ClientUserId, type: 541));

        // Publishing again is a state conflict, not a second notification.
        Assert.Equal(HttpStatusCode.Conflict, (await w.Office.PostAsJsonAsync($"{Office(w)}/updates/{update}/publish", new { NotifyClient = true })).StatusCode);

        // Sharing with notifyClient sends one "item shared" notification.
        Guid pdf = await UploadAsync(w, "notice.pdf", "application/pdf", "%PDF notice");
        HttpResponseMessage shared = await PostShareAsync(w, kind: 1, pdf, notifyClient: true);
        Assert.Equal(HttpStatusCode.Created, shared.StatusCode);
        await CrmPortalTestSeeding.DrainAsync(fixture);
        Assert.Equal(1, await CountNotificationsAsync(w.ClientUserId, type: 540));
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private async Task<(PortalWorld World, HttpClient Portal)> WorldAsync(string label)
    {
        string suffix = Guid.NewGuid().ToString("N")[..8];
        (Guid employeeId, string userId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"{label}-office-{suffix}@example.com");
        HttpClient office = CrmPortalTestSeeding.Client(fixture, userId, [.. CrmPortalTestSeeding.OfficeCodes, .. ExtraOfficeCodes]);
        PortalWorld world = await CrmPortalTestSeeding.CreateAsync(fixture, label, office, employeeId, userId);
        return (world, CrmPortalTestSeeding.Client(fixture, world.ClientUserId, PortalCodes));
    }

    private static string Office(PortalWorld w) => $"/api/crm/projects/{w.ProjectId}/portal";
    private static string Client(PortalWorld w) => $"/api/crm/portal/projects/{w.ProjectId}";
    private static string Overview(PortalWorld w) => $"{Client(w)}/overview";
    private static string Files(PortalWorld w) => $"{Client(w)}/files";
    private static string Gallery(PortalWorld w) => $"{Client(w)}/gallery";
    private static string Tasks(PortalWorld w) => $"{Client(w)}/tasks";
    private static string Updates(PortalWorld w) => $"{Client(w)}/updates";
    private static string File(PortalWorld w, Guid shareId) => $"{Client(w)}/files/{shareId}";

    private static IEnumerable<Guid> ShareIds(JsonElement list, string property = "shareId") =>
        list.EnumerateArray().Select(e => e.GetProperty(property).GetGuid());

    private static object SettingsBody(
        bool progress = false, bool tasks = false, bool documents = false, bool gallery = false, bool siteSurveys = false,
        bool updates = false) =>
        new { Progress = progress, Tasks = tasks, Documents = documents, Gallery = gallery, SiteSurveys = siteSurveys, Updates = updates };

    private static async Task SettingsAsync(
        PortalWorld w, bool progress = false, bool tasks = false, bool documents = false, bool gallery = false,
        bool siteSurveys = false, bool updates = false)
    {
        HttpResponseMessage response = await w.Office.PutAsJsonAsync($"{Office(w)}/settings",
            SettingsBody(progress, tasks, documents, gallery, siteSurveys, updates));
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    private static Task<HttpResponseMessage> PostShareAsync(
        PortalWorld w, int kind, Guid itemId, string? title = null, string? note = null, bool followLatest = true, bool notifyClient = false) =>
        w.Office.PostAsJsonAsync($"{Office(w)}/shares", new
        {
            ItemKind = kind,
            ItemId = itemId,
            ClientTitle = title,
            ClientNote = note,
            FollowLatest = followLatest,
            NotifyClient = notifyClient,
        });

    private static async Task<Guid> ShareAsync(
        PortalWorld w, int kind, Guid itemId, string? title = null, string? note = null, bool followLatest = true)
    {
        HttpResponseMessage response = await PostShareAsync(w, kind, itemId, title, note, followLatest);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("shareId").GetGuid();
    }

    private static async Task<Guid> CreateUpdateAsync(PortalWorld w, string title, string body, Guid[] shareIds)
    {
        HttpResponseMessage response = await w.Office.PostAsJsonAsync($"{Office(w)}/updates", new { Title = title, Body = body, ShareIds = shareIds });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("updateId").GetGuid();
    }

    private static async Task PublishUpdateAsync(PortalWorld w, string title, string body, Guid[] shareIds, bool notify)
    {
        Guid id = await CreateUpdateAsync(w, title, body, shareIds);
        HttpResponseMessage response = await w.Office.PostAsJsonAsync($"{Office(w)}/updates/{id}/publish", new { NotifyClient = notify });
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> CreateTaskAsync(PortalWorld w, string name)
    {
        HttpResponseMessage response = await w.Office.PostAsJsonAsync($"/api/projects/{w.ProjectId}/tasks", new { ProjectStageId = w.StageId, Name = name });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("taskId").GetGuid();
    }

    private static async Task<Guid> UploadAsync(PortalWorld w, string name, string contentType, string text)
    {
        HttpResponseMessage response = await w.Office.PostAsync($"/api/projects/{w.ProjectId}/documents", Multipart(name, contentType, text));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetGuid();
    }

    private static async Task ReviseAsync(Guid documentId, PortalWorld w, string name, string text)
    {
        HttpResponseMessage response = await w.Office.PostAsync($"/api/project-documents/{documentId}/revisions", Multipart(name, "application/pdf", text));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static MultipartFormDataContent Multipart(string name, string contentType, string text)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { content, "file", name } };
    }

    private async Task<int> CountNotificationsAsync(string recipient, int type)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """SELECT COUNT(*) FROM notify.notifications WHERE "RecipientUserId" = @id AND "Type" = @type""", connection);
        command.Parameters.AddWithValue("id", recipient);
        command.Parameters.AddWithValue("type", type);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    /// <summary>Puts the update's published event back on the outbox and forgets its inbox row — what a crash after
    /// the notification committed, and before the handler's inbox row did, leaves behind.</summary>
    private async Task RedeliverAsync(Guid updateId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            WITH target AS (
                SELECT id FROM shared.outbox_messages
                WHERE type LIKE '%CrmPortalUpdatePublishedIntegrationEvent%' AND content::text LIKE '%' || @update || '%')
            , cleared AS (DELETE FROM shared.inbox_messages WHERE event_id IN (SELECT id FROM target) RETURNING 1)
            UPDATE shared.outbox_messages SET processed_utc = NULL WHERE id IN (SELECT id FROM target)
            """, connection);
        command.Parameters.AddWithValue("update", updateId.ToString());
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }
}
