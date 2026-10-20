namespace BuildingBlocks.Outbox;

/// <summary>
/// Wakes <see cref="OutboxProcessor"/> immediately after a commit that wrote a new outbox row, instead
/// of waiting out the fallback poll interval. A bounded (0,1) semaphore, not a counter — multiple
/// nudges before the processor wakes collapse to one wake-up, which is correct: the processor drains
/// every pending row on each pass (<see cref="OutboxProcessor.ProcessPendingAsync"/>), so a second
/// pending row doesn't need a second wake-up to be picked up.
///
/// Registered as a singleton (see <see cref="OutboxServiceCollectionExtensions.AddOutbox"/>) — one
/// signal shared by every request's scoped decorator instance and the single background processor.
///
/// Per docs/low-resource-hosting.md §2: this is the optimization, not the correctness guarantee. The
/// fallback poll in <see cref="OutboxProcessor"/> is what still delivers a row written by a crashed
/// request (whose nudge never fired) or by a second process instance (whose in-memory signal this one
/// never sees) — never remove it in exchange for this.
/// </summary>
public sealed class OutboxSignal : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    /// <summary>Call after the transaction that wrote the outbox row has committed — never before. A
    /// nudge on an uncommitted row races the processor into finding nothing, defeating the point.</summary>
    public void Nudge()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // A wake-up is already pending; this one is redundant. Same reasoning as above - the
            // processor drains everything pending in one pass, so collapsing to one wake-up is correct.
        }
    }

    /// <summary>Waits for the next nudge, or <paramref name="fallback"/>, whichever comes first.</summary>
    public Task WaitAsync(TimeSpan fallback, CancellationToken ct) => _signal.WaitAsync(fallback, ct);

    /// <summary>The container disposes the singleton at shutdown, after the hosted processor has stopped.</summary>
    public void Dispose() => _signal.Dispose();
}
