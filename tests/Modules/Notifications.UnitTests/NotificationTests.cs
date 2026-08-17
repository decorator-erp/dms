using Notifications.Domain;
using Xunit;

namespace Notifications.UnitTests;

/// <summary>
/// N1-03 verify: the dedupe semantics, as a <b>domain</b> property rather than only an index.
///
/// <para>The unique index in the <c>notify</c> schema enforces this at the database. But the index can
/// only enforce whatever key it is handed — the rule that the stored key is scoped
/// <i>per recipient</i> lives in <see cref="Notification.ScopeDedupeKey"/>, and getting that wrong
/// produces a database that is behaving perfectly while the system is wrong. If the key were stored
/// unscoped, the index would let the FIRST recipient of a fan-out receive a reminder and silently drop
/// it for everyone else; if it were scoped by something other than the recipient, re-running a job
/// would duplicate. Neither failure is visible in the schema.</para>
///
/// <para>This is the property the whole consolidated-sweep design rests on — one timer re-running
/// seven jobs is only safe because re-running one is a no-op.</para>
/// </summary>
public class NotificationTests
{
    private const string LogicalKey = "task-2f1c-due--1d";
    private const string Alice = "user-alice";
    private const string Bob = "user-bob";

    [Fact]
    public void ScopeDedupeKey_GivesTheSameRecipientTheSameKey_SoARerunIsANoOp()
    {
        Assert.Equal(
            Notification.ScopeDedupeKey(LogicalKey, Alice),
            Notification.ScopeDedupeKey(LogicalKey, Alice));
    }

    [Fact]
    public void ScopeDedupeKey_GivesDifferentRecipientsDifferentKeys_SoOneReminderCanFanOut()
    {
        Assert.NotEqual(
            Notification.ScopeDedupeKey(LogicalKey, Alice),
            Notification.ScopeDedupeKey(LogicalKey, Bob));
    }

    /// <summary>
    /// A null logical key stays null rather than becoming <c>":user-alice"</c>. The unique index is
    /// filtered on <c>DedupeKey IS NOT NULL</c>, so this is what keeps un-keyed notifications
    /// unlimited: were null scoped into a real string, a user's second ever un-keyed notification
    /// would collide with their first and vanish.
    /// </summary>
    [Fact]
    public void ScopeDedupeKey_LeavesANullLogicalKeyNull_SoUnkeyedNotificationsNeverCollide()
    {
        Assert.Null(Notification.ScopeDedupeKey(null, Alice));
    }

    [Fact]
    public void Create_StoresTheScopedKey_NotTheLogicalOneItWasGiven()
    {
        var notification = Notification.Create(
            Alice, NotificationType.TaskDueSoon, "Task due", dedupeKey: LogicalKey);

        Assert.Equal(Notification.ScopeDedupeKey(LogicalKey, Alice), notification.DedupeKey);
        Assert.NotEqual(LogicalKey, notification.DedupeKey);
    }

    [Fact]
    public void Create_WithNoDedupeKey_LeavesItNull()
    {
        var notification = Notification.Create(Alice, NotificationType.TaskDueSoon, "Task due");

        Assert.Null(notification.DedupeKey);
    }

    [Fact]
    public void Create_StartsUnread()
    {
        var notification = Notification.Create(Alice, NotificationType.TaskAssigned, "Assigned");

        Assert.False(notification.IsRead);
        Assert.Null(notification.ReadAtUtc);
    }

    /// <summary>Idempotent by design: a double-click or a retried request must not rewrite when the
    /// user actually saw it.</summary>
    [Fact]
    public void MarkRead_IsIdempotent_AndKeepsTheOriginalReadTime()
    {
        var notification = Notification.Create(Alice, NotificationType.TaskAssigned, "Assigned");
        var first = new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc);
        var later = first.AddHours(3);

        notification.MarkRead(first);
        notification.MarkRead(later);

        Assert.True(notification.IsRead);
        Assert.Equal(first, notification.ReadAtUtc);
    }
}
