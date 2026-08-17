using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Notifications.Infrastructure.Notifications;

/// <summary>
/// Real-time notification channel. Each connection joins the caller's own user group
/// (<c>"user:{userId}"</c>) on connect, so <see cref="NotificationService"/> can target one person
/// across all their open tabs and devices without tracking connection ids.
///
/// <para><b>No backplane, deliberately.</b> Group membership is per-process. On the single-instance
/// hosting target that is exactly right and costs one WebSocket per active user. On a second instance
/// it degrades: a client connected to instance A misses a push originating on instance B. The row is
/// still written, so a refresh recovers — what is lost is real-time delivery, not data. The fix at that
/// point is a Redis backplane; recorded in <c>docs/low-resource-hosting.md</c> so that day does not
/// begin with rediscovering it.</para>
///
/// <para><c>Context.UserIdentifier</c> resolves from <c>ClaimTypes.NameIdentifier</c>, which
/// JwtBearer's default inbound mapping produces from the token's <c>sub</c> claim — true for both the
/// real <c>JwtProvider</c> and the test issuer. Browsers cannot set an <c>Authorization</c> header on a
/// WebSocket handshake, so the token arrives as <c>?access_token=</c>; the handler that reads it is in
/// <c>Api.DI</c>, scoped to <c>/hubs</c> paths only.</para>
/// </summary>
[Authorize]
internal sealed class NotificationsHub : Hub
{
    public const string Route = "/hubs/notifications";

    /// <summary>Client-side callback name for pushed notifications.</summary>
    public const string ReceiveMethod = "ReceiveNotification";

    public static string UserGroup(string userId) => $"user:{userId}";

    public override async Task OnConnectedAsync()
    {
        if (Context.UserIdentifier is { Length: > 0 } userId)
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));

        await base.OnConnectedAsync();
    }
}
