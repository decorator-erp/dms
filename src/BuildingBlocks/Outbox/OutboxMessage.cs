using System.Text.Json;
using BuildingBlocks.Events;

namespace BuildingBlocks.Outbox;

/// <summary>
/// A staged integration event, written to <c>shared.outbox_messages</c> through the publishing
/// module's own <see cref="Microsoft.EntityFrameworkCore.DbContext"/> (see
/// <see cref="DbContextOutboxExtensions.Outbox"/>) so it commits in the same transaction as the
/// aggregate write that raised it.
///
/// <para>Its life: pending → processed, or pending → failed → waiting (backoff) → pending … → dead-lettered.
/// A message is due when <see cref="NextAttemptUtc"/> has passed; that starts as the event's own
/// occurrence time and moves forward with each failure. A dead-lettered message is never selected again
/// until an operator resets it (see <see cref="OutboxProcessor"/>).</para>
/// </summary>
public sealed class OutboxMessage
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Guid Id { get; private set; }
    public string Type { get; private set; } = null!;
    public string Content { get; private set; } = null!;
    public DateTimeOffset OccurredUtc { get; private set; }
    public DateTimeOffset? ProcessedUtc { get; private set; }
    public int Attempts { get; private set; }
    public string? Error { get; private set; }

    /// <summary>When the processor may next try this message. Non-nullable on purpose: the sweep selects
    /// <c>next_attempt_utc &lt;= now</c>, which never matches NULL, and the column defaults to <c>now()</c>
    /// so a row written any other way is due at once.</summary>
    public DateTimeOffset NextAttemptUtc { get; private set; }

    /// <summary>Set when the processor gives up on the message: its payload can't be read, or it failed
    /// <c>Outbox:MaxAttempts</c> times.</summary>
    public DateTimeOffset? DeadLetteredUtc { get; private set; }

    private OutboxMessage()
    {
        // EF materialization.
    }

    internal static OutboxMessage From(IIntegrationEvent @event, Type eventType) => new()
    {
        Id = @event.EventId,
        Type = eventType.AssemblyQualifiedName
            ?? throw new InvalidOperationException($"'{eventType}' has no assembly-qualified name."),
        Content = JsonSerializer.Serialize(@event, eventType, JsonOptions),
        OccurredUtc = @event.OccurredAtUtc,
        NextAttemptUtc = @event.OccurredAtUtc,
        Attempts = 0,
    };

    public void MarkProcessed(DateTimeOffset processedUtc) => ProcessedUtc = processedUtc;

    /// <summary>Counts a failed attempt and puts the message off until <paramref name="nowUtc"/> plus
    /// <paramref name="backoff"/>.</summary>
    public void RecordFailure(string error, DateTimeOffset nowUtc, TimeSpan backoff)
    {
        Attempts++;
        Error = error;
        NextAttemptUtc = nowUtc + backoff;
    }

    public void MarkDeadLettered(DateTimeOffset nowUtc, string error)
    {
        Error = error;
        DeadLetteredUtc = nowUtc;
    }
}
