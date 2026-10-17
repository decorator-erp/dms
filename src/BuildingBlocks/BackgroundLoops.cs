using Microsoft.Extensions.Configuration;

namespace BuildingBlocks;

/// <summary>
/// The one switch that stops <c>OutboxProcessor</c> and <c>PeriodicJobWorker</c> running their timer
/// loops. <b>Default is ON</b> — a missing or unparseable value keeps both loops running, so no
/// deployment can accidentally disable delivery by omitting a setting.
///
/// <para><b>Why this exists (F1-10).</b> Both workers are driven directly by tests — ten call sites
/// invoke <c>ProcessPendingAsync</c> or <c>RunAllJobsAsync</c> explicitly, because a test that waits
/// for a timer is a slow test that still races. Nothing in the test host relies on the loops. What the
/// loops DO contribute there is SQL arriving at unpredictable moments in a process-wide sink, which is
/// precisely what the schema-isolation tests measure: <c>ApiFixture.CapturedSql</c> is shared across
/// the whole host, so a job sweep landing between <c>ClearCapturedSql()</c> and the assertion reads as
/// the module under test having touched another schema.</para>
///
/// <para><b>That flake was diagnosed at F1-00 and deliberately left until now.</b> The obvious
/// "fix" — widening the forbidden-schema list until the noise stops — would hide exactly the leaks
/// those tests exist to catch, which is why it was not taken. Standing the loops down removes the
/// interleave instead of reasoning about which half of it is observable, the same argument
/// <c>PeriodicJobWorker._sweepGate</c> already makes for its own re-entrancy.</para>
///
/// <para>Production never sets this. The only caller is <c>Integration.Tests.ApiFixture</c>, and the
/// setting name is spelled out there so the connection is greppable from both ends.</para>
/// </summary>
public static class BackgroundLoops
{
    public const string EnabledKey = "BackgroundLoops:Enabled";

    public static bool Enabled(IConfiguration configuration) =>
        !bool.TryParse(configuration[EnabledKey], out bool enabled) || enabled;
}
