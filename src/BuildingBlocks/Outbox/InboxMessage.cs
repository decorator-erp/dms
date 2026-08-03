namespace BuildingBlocks.Outbox;

/// <summary>
/// Records that a specific handler has already processed a specific event — the de-dupe row
/// <see cref="InboxDedupeDecorator{TContext,TEvent}"/> writes atomically alongside the handler's own
/// business write, through the SAME module <c>DbContext</c>. Primary key is (EventId, HandlerName):
/// one event can have several subscribing handlers across several modules, each tracked independently.
/// </summary>
public sealed class InboxMessage
{
    public Guid EventId { get; private set; }
    public string HandlerName { get; private set; } = null!;
    public DateTimeOffset ProcessedUtc { get; private set; }

    private InboxMessage()
    {
        // EF materialization.
    }

    public InboxMessage(Guid eventId, string handlerName, DateTimeOffset processedUtc)
    {
        EventId = eventId;
        HandlerName = handlerName;
        ProcessedUtc = processedUtc;
    }
}
