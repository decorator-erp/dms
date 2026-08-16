using System.ComponentModel.DataAnnotations;

namespace Crm.Infrastructure.Services;

/// <summary>
/// M1-01: office-hours configuration for <see cref="CrmSchedulerService"/>, bound and validated at
/// startup in <c>CrmModule.Register</c> — the module's first bound options, matching the
/// <c>JwtOptions</c> pattern from <c>Identity.Infrastructure</c>. Defaults mirror the backend's
/// <c>appsettings.json</c> (Sun-Thu, 09:00-17:00, 60-minute slots, Africa/Cairo).
///
/// <para><see cref="TimezoneId"/> cannot be checked by a data annotation — a malformed IANA/Windows id
/// only fails when <c>TimeZoneInfo.FindSystemTimeZoneById</c> is actually called. The registration adds
/// a <c>.Validate(...)</c> alongside <c>.ValidateDataAnnotations()</c> so a bad value fails the app at
/// boot instead of on the first call to <see cref="CrmSchedulerService.GetFreeSlotsAsync"/>.</para>
/// </summary>
internal sealed class OfficeHoursOptions
{
    public const string SectionName = "OfficeHours";

    /// <summary>0 = Sunday ... 6 = Saturday, matching <see cref="DayOfWeek"/>'s own numbering.</summary>
    [Required, MinLength(1)]
    public int[] WorkingDays { get; init; } = [0, 1, 2, 3, 4];

    [Required, RegularExpression(@"^\d{2}:\d{2}$")]
    public string DayStartLocal { get; init; } = "09:00";

    [Required, RegularExpression(@"^\d{2}:\d{2}$")]
    public string DayEndLocal { get; init; } = "17:00";

    [Range(15, 240)]
    public int SlotMinutes { get; init; } = 60;

    [Required]
    public string TimezoneId { get; init; } = "Africa/Cairo";
}
