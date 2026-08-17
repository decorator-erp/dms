using SharedKernel;

namespace Notifications.Domain.Errors;

internal static class NotificationErrors
{
    /// <summary>
    /// Returned when a notification id does not exist <b>or</b> belongs to another user — deliberately
    /// one error, not two. Distinguishing "no such notification" from "not yours" would let any
    /// authenticated caller enumerate other people's notification ids by probing status codes, which is
    /// the same reasoning behind the module-scope decorators' 404-not-403 rule.
    /// </summary>
    public static readonly Error NotFound = Error.NotFound(
        "Notification.NotFound", "Notification not found.");
}

internal static class ActivityErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "Activity.NotFound", "Activity not found.");
}
