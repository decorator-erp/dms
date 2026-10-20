namespace Identity.Domain;

/// <summary>
/// N5: a record that a one-time seed step has run. A seeder that must run exactly once (rather than
/// "add whatever is missing" on every boot) checks for its key, does its work, and inserts the marker in
/// the same save. After that the step never runs again, so an admin who removes what it added keeps
/// the removal.
/// </summary>
internal sealed class SeedMarker
{
    private SeedMarker() { }

    public string Key { get; private set; } = null!;
    public DateTime AppliedAtUtc { get; private set; }

    public static SeedMarker Create(string key, DateTime appliedAtUtc) =>
        new() { Key = key, AppliedAtUtc = appliedAtUtc };
}
