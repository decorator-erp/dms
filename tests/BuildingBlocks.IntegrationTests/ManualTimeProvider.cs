namespace BuildingBlocks.IntegrationTests;

/// <summary>A clock the test moves by hand, so the outbox's backoff can be crossed without waiting.
/// It starts a minute ahead of the real time, because messages are stamped with the real time when they
/// are enqueued: a clock behind that would see a new message as not yet due, and a minute leaves room for
/// a slow machine between building the clock and enqueuing. Whole seconds, because Postgres keeps
/// microseconds and a tick-precise clock would make every round-tripped wait off by a fraction.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
