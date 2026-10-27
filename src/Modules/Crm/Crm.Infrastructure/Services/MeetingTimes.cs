using System.Globalization;

namespace Crm.Infrastructure.Services;

/// <summary>B-13: how a meeting is written in a notification. One place, so the scheduled, rescheduled,
/// canceled and reminder texts always agree.</summary>
internal static class MeetingTimes
{
    /// <summary>The notification title column's length (Notifications' configuration).</summary>
    private const int TitleMaxLength = 256;

    public static string Display(DateTime utc) =>
        utc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    /// <summary>"{prefix} — {meeting title}", cut to the column: a 256-character meeting title would
    /// otherwise overflow the notification title and fail the send on every attempt.</summary>
    public static string Title(string prefix, string meetingTitle)
    {
        string title = $"{prefix} — {meetingTitle}";
        return title.Length <= TitleMaxLength ? title : string.Concat(title.AsSpan(0, TitleMaxLength - 1), "…");
    }
}
