using System.Globalization;

namespace Crm.Infrastructure.Services;

/// <summary>B-13: how a meeting is written in a notification. One place, so the scheduled, rescheduled,
/// canceled and reminder texts always agree.</summary>
internal static class MeetingTimes
{
    /// <summary>The notification title column's length (Notifications' configuration).</summary>
    private const int TitleMaxLength = 256;

    /// <summary>B-15 (MT-N8): the time in the office zone (<see cref="OfficeHoursOptions.TimezoneId"/>), with
    /// the zone named — "2026-11-02 14:00 (Africa/Cairo)". An unknown zone id falls back to UTC rather
    /// than failing the notification.</summary>
    public static string Display(DateTime utc, string timeZoneId)
    {
        var instant = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        try
        {
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(instant, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
            return $"{local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} ({timeZoneId})";
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return $"{instant.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} (UTC)";
        }
    }

    /// <summary>"{prefix} — {meeting title}", cut to the column: a 256-character meeting title would
    /// otherwise overflow the notification title and fail the send on every attempt.</summary>
    public static string Title(string prefix, string meetingTitle)
    {
        string title = $"{prefix} — {meetingTitle}";
        return title.Length <= TitleMaxLength ? title : string.Concat(title.AsSpan(0, TitleMaxLength - 1), "…");
    }
}
