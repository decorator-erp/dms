using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Contracts;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// N1-04 verify: fan-out, idempotence, and real-time delivery, through the real host.
///
/// <para>Everything here resolves <see cref="INotificationService"/> from the host container rather
/// than constructing it — the service is <c>internal</c> to <c>Notifications.Infrastructure</c>, and the
/// only public thing about it is the contract, which is the point.</para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationDeliveryTests(ApiFixture fixture)
{
    /// <summary>
    /// <b>The property the whole consolidated-sweep design rests on.</b> One logical reminder fans out
    /// to three recipients as three rows; re-running it produces <b>zero</b> new rows.
    ///
    /// <para>The second run's <i>delta</i> is what is asserted, not the total. A total-only assertion
    /// would pass just as happily if the second run had written three rows and something else had
    /// deleted three.</para>
    /// </summary>
    [Fact]
    public async Task OneReminderToThreeRecipients_WritesThreeRows_AndReRunningWritesNone()
    {
        string[] recipients = await Task.WhenAll(
            RegisterUserAsync("fanout-a"), RegisterUserAsync("fanout-b"), RegisterUserAsync("fanout-c"));
        string dedupeKey = $"task-{Guid.NewGuid():N}-due--1d";

        await NotifyAsync(recipients, dedupeKey);
        int afterFirstRun = await CountNotificationsAsync(recipients);

        await NotifyAsync(recipients, dedupeKey);
        int afterSecondRun = await CountNotificationsAsync(recipients);

        Assert.Equal(3, afterFirstRun);
        Assert.Equal(afterFirstRun, afterSecondRun);
    }

    /// <summary>
    /// The dedupe key is scoped per recipient, so two DIFFERENT people receiving the same logical
    /// reminder is not a collision. This is the other half of the property above: without per-recipient
    /// scoping the unique index would let the first recipient through and silently drop the rest, and
    /// the test above would still pass on a total of one.
    /// </summary>
    [Fact]
    public async Task TheSameLogicalKey_ReachesEveryRecipientSeparately()
    {
        string first = await RegisterUserAsync("scope-a");
        string second = await RegisterUserAsync("scope-b");
        string dedupeKey = $"stage-{Guid.NewGuid():N}-due-0d";

        await NotifyAsync([first], dedupeKey);
        await NotifyAsync([second], dedupeKey);

        Assert.Equal(1, await CountNotificationsAsync([first]));
        Assert.Equal(1, await CountNotificationsAsync([second]));
    }

    /// <summary>
    /// Notifications with no dedupe key are unlimited — the unique index is filtered on
    /// <c>IS NOT NULL</c> precisely so one-off, event-driven notifications never collide on NULL.
    /// Without that filter a user's second ever un-keyed notification would vanish.
    /// </summary>
    [Fact]
    public async Task NotificationsWithoutADedupeKey_AreNeverDeduplicated()
    {
        string recipient = await RegisterUserAsync("unkeyed");

        await NotifyAsync([recipient], dedupeKey: null);
        await NotifyAsync([recipient], dedupeKey: null);

        Assert.Equal(2, await CountNotificationsAsync([recipient]));
    }

    /// <summary>
    /// <b>The narrowest possible proof that <c>Api.DI</c>'s <c>OnMessageReceived</c> handler fires.</b>
    /// SignalR's browser client cannot set an <c>Authorization</c> header on a WebSocket handshake, so
    /// it appends <c>?access_token=</c>. Without the handler every real client gets 401 and the hub is
    /// decorative — and nothing else in the suite would notice.
    ///
    /// <para>Both directions asserted: with the query-string token the negotiate succeeds, without it
    /// the same request is rejected. The negative half is what stops this passing because the endpoint
    /// is accidentally anonymous.</para>
    /// </summary>
    [Fact]
    public async Task HubNegotiate_AcceptsTheQueryStringToken_AndRejectsAnUnauthenticatedRequest()
    {
        HttpClient client = fixture.CreateClient();
        string token = fixture.IssueTestToken(Guid.NewGuid().ToString());

        HttpResponseMessage withToken = await client.PostAsync(
            $"/hubs/notifications/negotiate?negotiateVersion=1&access_token={token}", null);
        HttpResponseMessage withoutToken = await client.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1", null);

        Assert.Equal(HttpStatusCode.OK, withToken.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, withoutToken.StatusCode);
    }

    /// <summary>
    /// A connected client receives a push for its OWN user and not for another's.
    ///
    /// <para>Long-polling over <c>TestServer</c>'s handler rather than a real WebSocket: long polling
    /// sends the bearer header normally, so this exercises <c>[Authorize]</c>,
    /// <c>Context.UserIdentifier</c> and <c>Groups.AddToGroupAsync</c> — the parts that decide WHO gets
    /// a message — without WebSocket plumbing. The query-string half of the auth story is covered
    /// separately above, which is why these are two tests and not one.</para>
    ///
    /// <para>A fake <c>IHubContext</c> would prove <c>NotificationService</c> calls the right group
    /// NAME, and nothing about whether a client ever joins that group. That gap is the whole risk here,
    /// so the real client is worth the setup.</para>
    /// </summary>
    [Fact]
    public async Task AConnectedClient_ReceivesItsOwnPush_AndNotAnotherUsersReceives()
    {
        string mine = await RegisterUserAsync("push-mine");
        string theirs = await RegisterUserAsync("push-theirs");

        await using HubConnection myConnection = BuildConnection(mine);
        await using HubConnection theirConnection = BuildConnection(theirs);

        var myPush = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var theirPush = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        myConnection.On<object>(NotificationsHubContract.ReceiveMethod, _ => myPush.TrySetResult("received"));
        theirConnection.On<object>(NotificationsHubContract.ReceiveMethod, _ => theirPush.TrySetResult("received"));

        await myConnection.StartAsync();
        await theirConnection.StartAsync();

        await NotifyAsync([mine], dedupeKey: null);

        Task delivered = await Task.WhenAny(myPush.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(myPush.Task, delivered);

        // The other user's connection is live and joined its own group; it simply is not a member of
        // the group this notification targeted. A short wait, because proving a NEGATIVE needs the
        // message to have had time to arrive if it were going to.
        Task leaked = await Task.WhenAny(theirPush.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.NotSame(theirPush.Task, leaked);
    }

    private HubConnection BuildConnection(string userId) =>
        new HubConnectionBuilder()
            .WithUrl($"{fixture.Server.BaseAddress}hubs/notifications", options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => fixture.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(fixture.IssueTestToken(userId));
            })
            .Build();

    private Task<string> RegisterUserAsync(string prefix) =>
        IdentityTestSeeding.ProvisionActiveUserAsync(fixture, $"{prefix}-{Guid.NewGuid():N}@example.com");

    private async Task NotifyAsync(IReadOnlyCollection<string> recipients, string? dedupeKey)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        INotificationService notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

        await notifications.NotifyManyAsync(
            recipients,
            NotificationType.TaskDueSoon,
            "Task due within 1 day(s)",
            body: "Integration test notification.",
            entityType: NotificationEntityType.ProjectTask,
            entityId: Guid.NewGuid(),
            dedupeKey: dedupeKey,
            ct: CancellationToken.None);
    }

    /// <summary>
    /// Counted straight from Postgres rather than through a query handler, because the read endpoints
    /// do not exist until N1-07 — and a delivery test should not be blocked on the surface that reads
    /// what it delivered.
    /// </summary>
    private async Task<int> CountNotificationsAsync(IReadOnlyCollection<string> recipients)
    {
        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new Npgsql.NpgsqlCommand(
            """SELECT COUNT(*) FROM notify.notifications WHERE "RecipientUserId" = ANY(@ids)""", connection);
        command.Parameters.AddWithValue("ids", recipients.ToArray());

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed record RegisterResponse(string UserId);
}

/// <summary>The hub's client-callback name, duplicated here because <c>NotificationsHub</c> is
/// <c>internal</c> to its module — as every hub and endpoint in this solution is. One string, and the
/// test would fail loudly if it drifted.</summary>
internal static class NotificationsHubContract
{
    public const string ReceiveMethod = "ReceiveNotification";
}
