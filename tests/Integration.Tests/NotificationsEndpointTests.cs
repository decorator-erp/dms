using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Identity.Contracts.Auth;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Contracts;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// N1-07 verify: the eighth module's surface.
///
/// <para>The eight-module and permission-total assertions live in <c>CrmSmokeTests</c> and
/// <c>SitesSmokeTests</c> and moved at N1-04, when <c>Program.cs</c> actually loaded the assembly —
/// registration is what those assert, and it does not wait for endpoints. What is new here is the
/// surface itself, and above all the <c>audit.read</c> / <c>audit.view_own</c> split.</para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationsEndpointTests(ApiFixture fixture)
{
    /// <summary>
    /// <b>The phase's only real authorization surface.</b> A <c>view_own</c> holder must not reach the
    /// full log, the by-entity history, or another user's trail — not "reach them filtered", not reach
    /// them at all. Asserted route by route rather than by sampling one, because the risk is a single
    /// route being gated on the wrong constant and nothing else noticing.
    /// </summary>
    [Theory]
    [InlineData("/api/audit")]
    [InlineData("/api/audit/entity/ApplicationUser/some-id")]
    [InlineData("/api/audit/user/someone-else")]
    public async Task AuditViewOwn_CannotReachAnyRouteThatReturnsAnotherUsersRows(string route)
    {
        var client = AuthenticatedClient(Guid.NewGuid().ToString(), NotificationsPermissions.AuditViewOwn);

        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// The other direction, so the test above cannot be passing because every audit route is broken:
    /// <c>audit.read</c> reaches all three.
    /// </summary>
    [Theory]
    [InlineData("/api/audit")]
    [InlineData("/api/audit/entity/ApplicationUser/some-id")]
    [InlineData("/api/audit/user/someone-else")]
    public async Task AuditRead_ReachesEveryAdminAuditRoute(string route)
    {
        var client = AuthenticatedClient(Guid.NewGuid().ToString(), NotificationsPermissions.AuditRead);

        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// <b>The scoping is server-side and unforgeable.</b> <c>/my-activity</c> returns the caller's rows
    /// and nobody else's, and there is no parameter through which another user's id could be supplied —
    /// the handler takes its <c>UserId</c> from <c>ICurrentUser</c> only.
    ///
    /// <para>Two real users, each with a real audited write, then each reads <c>/my-activity</c>: every
    /// row must carry the reader's own id. Trying to pass <c>?userId=</c> is included deliberately —
    /// it is the obvious attack, and it must be ignored rather than honoured.</para>
    /// </summary>
    [Fact]
    public async Task MyActivity_ReturnsOnlyTheCallersOwnRows_AndIgnoresAUserIdParameter()
    {
        var mine = await RegisterAndAuditAsync();
        var theirs = await RegisterAndAuditAsync();

        var client = AuthenticatedClient(mine, NotificationsPermissions.AuditViewOwn);

        var own = await ReadUserIdsAsync(client, "/api/audit/my-activity?pageSize=100");
        Assert.NotEmpty(own);
        Assert.All(own, userId => Assert.Equal(mine, userId));

        // The obvious attempt to widen it. The parameter is not bound by the route at all, so it must
        // change nothing rather than be honoured.
        var spoofed = await ReadUserIdsAsync(client, $"/api/audit/my-activity?userId={theirs}&pageSize=100");
        Assert.All(spoofed, userId => Assert.Equal(mine, userId));
    }

    [Fact]
    public async Task NotificationRoutes_AreScopedToTheCaller_AndRequireTheOwnPermission()
    {
        var recipient = await RegisterUserAsync();
        var stranger = await RegisterUserAsync();
        await NotifyAsync(recipient);

        var recipientClient = AuthenticatedClient(recipient, NotificationsPermissions.ViewOwn);
        var strangerClient = AuthenticatedClient(stranger, NotificationsPermissions.ViewOwn);

        Assert.Equal(1, await ReadUnreadCountAsync(recipientClient));
        Assert.Equal(0, await ReadUnreadCountAsync(strangerClient));

        // Without the permission the surface is closed, even to the rightful owner.
        var unpermitted = AuthenticatedClient(recipient);
        Assert.Equal(HttpStatusCode.Forbidden, (await unpermitted.GetAsync("/api/notifications")).StatusCode);
    }

    /// <summary>
    /// Marking read is idempotent and scoped: a stranger cannot mark someone else's notification read by
    /// guessing its id, and gets 404 rather than 403 — distinguishing the two would let any
    /// authenticated caller enumerate other people's notification ids by probing status codes.
    /// </summary>
    [Fact]
    public async Task MarkRead_IsScopedToTheOwner_AndAStrangerGetsNotFoundRatherThanForbidden()
    {
        var recipient = await RegisterUserAsync();
        var stranger = await RegisterUserAsync();
        await NotifyAsync(recipient);

        var recipientClient = AuthenticatedClient(recipient, NotificationsPermissions.ViewOwn);
        var notificationId = (await ReadFirstNotificationAsync(recipientClient)).GetProperty("id").GetGuid();

        var strangerClient = AuthenticatedClient(stranger, NotificationsPermissions.ViewOwn);
        var strangerAttempt = await strangerClient.PutAsync($"/api/notifications/{notificationId}/read", null);
        Assert.Equal(HttpStatusCode.NotFound, strangerAttempt.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await recipientClient.PutAsync($"/api/notifications/{notificationId}/read", null)).StatusCode);
        Assert.Equal(0, await ReadUnreadCountAsync(recipientClient));
    }

    /// <summary>Every route this task added is actually mapped — the OpenAPI document is the host's own
    /// account of what it serves, so a route that failed to map is absent from it.</summary>
    [Fact]
    public async Task OpenApiDocument_ListsEveryNotificationsRoute()
    {
        var document = await fixture.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = document.GetProperty("paths");

        foreach (var route in new[]
        {
            "/api/notifications",
            "/api/notifications/unread-count",
            "/api/notifications/{notificationId}/read",
            "/api/notifications/read-all",
            "/api/activities",
            "/api/audit",
            "/api/audit/entity/{entityName}/{entityId}",
            "/api/audit/user/{userId}",
            "/api/audit/my-activity",
        })
        {
            Assert.True(paths.TryGetProperty(route, out _), $"{route} is missing from /openapi/v1.json.");
        }
    }

    private HttpClient AuthenticatedClient(string userId, params string[] permissions)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", fixture.IssueTestToken(userId, permissions));
        return client;
    }

    private async Task<string> RegisterUserAsync()
    {
        var response = await fixture.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            $"endpoint-{Guid.NewGuid():N}@example.com", "P@ssw0rd123!", "Endpoint", "User", null, "employee"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetString()!;
    }

    /// <summary>Registers a user and makes an audited write attributed to them, so
    /// <c>/my-activity</c> has something of theirs to return.</summary>
    private async Task<string> RegisterAndAuditAsync()
    {
        var userId = await RegisterUserAsync();
        var client = AuthenticatedClient(userId, Hr.Contracts.HrPermissions.EmployeesCreate);

        var response = await client.PostAsJsonAsync("/api/employees", new
        {
            AppUserId = userId,
            JobTitle = "Auditor",
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ContractType = "FullTime",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return userId;
    }

    private async Task NotifyAsync(string recipientUserId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<INotificationService>().NotifyAsync(
            recipientUserId, NotificationType.TaskAssigned, "Endpoint test", ct: CancellationToken.None);
    }

    private static async Task<List<string>> ReadUserIdsAsync(HttpClient client, string route)
    {
        var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        return [.. page.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("userId").GetString())
            .Where(id => id is not null)
            .Select(id => id!)];
    }

    private static async Task<int> ReadUnreadCountAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/notifications/unread-count");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<int>();
    }

    private static async Task<JsonElement> ReadFirstNotificationAsync(HttpClient client)
    {
        var page = await client.GetFromJsonAsync<JsonElement>("/api/notifications");
        return page.GetProperty("items").EnumerateArray().First();
    }
}
