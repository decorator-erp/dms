using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>UI-1: <c>GET /api/crm/portal/meetings/free-slots</c> — the office's free slots for the calling client's
/// own project, on <c>crm.meetings.request_own</c> (a client never holds <c>crm.meetings.read</c>). Another client's
/// project is the portal's 404; the answer is the office route's, field for field.</summary>
[Collection(ApiCollection.Name)]
public sealed class CrmPortalFreeSlotsRouteTests(ApiFixture fixture)
{
    private const string Route = "/api/crm/portal/meetings/free-slots";

    // A future window, so neither route clamps its start to "now" between the two calls.
    private static readonly DateTime From = DateTime.UtcNow.Date.AddDays(1);
    private static readonly DateTime To = From.AddDays(7);

    [Fact]
    public async Task Client_with_request_own_gets_the_office_free_slots_for_its_own_project()
    {
        PortalWorld w = await CrmPortalTestSeeding.CreateAsync(fixture, "fs-own");
        HttpClient portal = CrmPortalTestSeeding.Client(fixture, w.ClientUserId, "crm.portal", "crm.meetings.request_own");

        HttpResponseMessage response = await portal.GetAsync(Url(Route, w.ProjectId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement slots = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEmpty(slots.EnumerateArray());
        JsonElement first = slots.EnumerateArray().First();
        Assert.True(first.GetProperty("startsAtUtc").GetDateTime() < first.GetProperty("endsAtUtc").GetDateTime());

        JsonElement office = await w.Office.GetFromJsonAsync<JsonElement>(Url("/api/crm/meetings/free-slots", w.ProjectId));
        Assert.Equal(office.GetRawText(), slots.GetRawText());
    }

    [Fact]
    public async Task Another_clients_project_is_not_found()
    {
        PortalWorld a = await CrmPortalTestSeeding.CreateAsync(fixture, "fs-a");
        PortalWorld b = await CrmPortalTestSeeding.CreateAsync(fixture, "fs-b", a.Office, a.OfficeEmployeeId, a.OfficeUserId);
        HttpClient portal = CrmPortalTestSeeding.Client(fixture, a.ClientUserId, "crm.portal", "crm.meetings.request_own");

        Assert.Equal(HttpStatusCode.NotFound, (await portal.GetAsync(Url(Route, b.ProjectId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await portal.GetAsync(Url(Route, Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task A_caller_without_request_own_is_forbidden()
    {
        PortalWorld w = await CrmPortalTestSeeding.CreateAsync(fixture, "fs-403");

        // A client with only the view permissions, and an office user with crm.meetings.read: neither holds request_own.
        HttpClient viewOnly = CrmPortalTestSeeding.Client(fixture, w.ClientUserId, "crm.portal", "crm.meetings.view_own");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewOnly.GetAsync(Url(Route, w.ProjectId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Office.GetAsync(Url(Route, w.ProjectId))).StatusCode);
    }

    private static string Url(string route, Guid projectId) =>
        $"{route}?projectId={projectId}&fromUtc={Iso(From)}&toUtc={Iso(To)}";

    private static string Iso(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
