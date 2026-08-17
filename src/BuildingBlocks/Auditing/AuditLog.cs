namespace BuildingBlocks.Auditing;

/// <summary>
/// One field-level change record, written to <c>shared.audit_logs</c> through the SAME
/// <see cref="Microsoft.EntityFrameworkCore.DbContext"/> as the change it audits — so it commits in
/// the same transaction. That is the whole reason the audit trail reuses the outbox's shape rather
/// than writing through a context of its own: an audit row in a separate transaction means a crash
/// between the two produces either a change with no trail or a trail with no change.
///
/// <para><b><see cref="Record"/> is <c>internal</c>, and that is load-bearing.</b> Its only call site
/// is <c>AuditSaveChangesInterceptor</c>. No caller outside this assembly can construct an audit row,
/// so no caller can pass a wrong entity name, a wrong action, or a wrong id — those three come from
/// EF metadata and <c>EntityState</c>, never from a literal. Together with
/// <see cref="AuditAction"/>'s three members, that is what closes B18 by mechanism rather than by
/// convention: the backend's defect was a hand-passed entity name paired with a hand-maintained
/// allow-list, and neither construct exists here to get wrong.</para>
///
/// <para>A plain class rather than a <c>SharedKernel.Entity</c>, matching
/// <see cref="Outbox.OutboxMessage"/>: an audit row raises no domain events, is never soft-deleted,
/// and is never updated after insert.</para>
/// </summary>
public sealed class AuditLog
{
    public Guid Id { get; private set; }

    /// <summary>The acting user's identity id, null for non-HTTP writes (seeding, the outbox
    /// processor's background scope, a periodic job). A null actor is deliberate: the row still
    /// records what changed and when, and refusing to write it would put the hole exactly where the
    /// trail matters most.</summary>
    public string? UserId { get; private set; }

    public string? UserEmail { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }

    public AuditAction Action { get; private set; }

    /// <summary>The CLR type name, read from <c>entry.Metadata.ClrType.Name</c>. Never a literal.</summary>
    public string EntityName { get; private set; } = null!;

    /// <summary>Primary key as text; composite keys are comma-joined in key-property order.</summary>
    public string? EntityId { get; private set; }

    /// <summary>JSON object of changed properties' prior values (Update/Delete), sensitive fields redacted.</summary>
    public string? OldValues { get; private set; }

    /// <summary>JSON object of changed properties' new values (Create/Update), sensitive fields redacted.</summary>
    public string? NewValues { get; private set; }

    /// <summary>JSON array of the property names that changed (Update only).</summary>
    public string? ChangedColumns { get; private set; }

    /// <summary>The owning module, resolved from the entity's EF schema — same source as
    /// <see cref="EntityName"/>, never a hand-maintained entity-to-module dictionary.</summary>
    public string? Module { get; private set; }

    public DateTimeOffset Timestamp { get; private set; }

    public string? RequestPath { get; private set; }
    public string? RequestMethod { get; private set; }

    private AuditLog()
    {
        // EF materialization.
    }

    internal static AuditLog Record(
        AuditAction action,
        string entityName,
        string? entityId,
        string? module,
        AuditContext context,
        DateTimeOffset timestamp,
        string? oldValues,
        string? newValues,
        string? changedColumns) => new()
        {
            Id = Guid.CreateVersion7(),
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            Module = module,
            UserId = context.UserId,
            UserEmail = context.UserEmail,
            IpAddress = context.IpAddress,
            UserAgent = context.UserAgent,
            RequestPath = context.RequestPath,
            RequestMethod = context.RequestMethod,
            Timestamp = timestamp,
            OldValues = oldValues,
            NewValues = newValues,
            ChangedColumns = changedColumns,
        };
}
