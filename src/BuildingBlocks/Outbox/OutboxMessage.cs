using System.Text.Json;
using BuildingBlocks.Events;

namespace BuildingBlocks.Outbox;

/// <summary>
/// A staged integration event, written to <c>shared.outbox_messages</c> through the publishing
/// module's own <see cref="Microsoft.EntityFrameworkCore.DbContext"/> (see
/// <see cref="DbContextOutboxExtensions.Outbox"/>) so it commits in the same transaction as the
/// aggregate write that raised it.
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
        Attempts = 0,
    };

    public void MarkProcessed(DateTimeOffset processedUtc) => ProcessedUtc = processedUtc;

    public void RecordFailure(string error)
    {
        Attempts++;
        Error = error;
    }
}
