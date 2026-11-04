namespace Projects.Application;

/// <summary>
/// H-dates: a project's dates are <c>timestamptz</c>, and Npgsql refuses to write a <see cref="DateTime"/> whose
/// kind is not <see cref="DateTimeKind.Utc"/>. The project form sends <c>YYYY-MM-DD</c>, which binds with kind
/// <see cref="DateTimeKind.Unspecified"/>: that is a date with no time zone, so it is stored as that day at
/// midnight UTC. A value with an offset binds as <see cref="DateTimeKind.Local"/> and is stored as its UTC
/// instant; a UTC value is unchanged. Lives in the Projects module, not SharedKernel (frozen).
/// </summary>
internal static class ProjectDates
{
    public static DateTime? ToUtc(DateTime? value) => value is { } d ? ToUtc(d) : null;

    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value.Date, DateTimeKind.Utc),
    };
}
