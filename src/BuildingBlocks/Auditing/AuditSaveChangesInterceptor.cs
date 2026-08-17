using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SharedKernel;

namespace BuildingBlocks.Auditing;

/// <summary>
/// Writes one <see cref="AuditLog"/> per changed <see cref="IAuditable"/> entity, into the SAME
/// <see cref="DbContext"/> that is saving — so the audit row and the change it audits commit or roll
/// back together. It <b>stages and never saves</b>: rows are added to the change tracker during
/// <c>SavingChanges</c> and EF persists them as part of the save already in flight. Calling
/// <c>SaveChangesAsync</c> here would open a second transaction and reintroduce exactly the gap this
/// design exists to close.
///
/// <para><b>No allow-list, and that is the point.</b> The backend gates on
/// <c>AuditMetadata.IsInScope(entityName)</c>, a hand-maintained <c>HashSet&lt;string&gt;</c>, next to
/// a second hand-maintained entity-to-module <c>Dictionary</c>. Its own source carries a red comment
/// recording what that cost: the CRM catalog was in the module dictionary but absent from the scope
/// set, so every catalog write "logged" and stored nothing — silently, with no error and nothing to
/// notice. That is B18. Implementing <see cref="IAuditable"/> IS the opt-in here: declared in the
/// entity's own file, checked by the compiler, discovered by <c>ChangeTracker.Entries&lt;IAuditable&gt;()</c>
/// with no second list to disagree with, and impossible to spell wrong.</para>
///
/// <para><b>Every string on the row is derived, none is passed.</b> <c>EntityName</c> is
/// <c>entry.Metadata.ClrType.Name</c>; <c>Module</c> is the entity's EF schema; <c>Action</c> is a
/// mapping of <see cref="EntityState"/>; <c>EntityId</c> is read from the primary key's own
/// properties. Rename the CLR type and the audit row follows, with no second place to update.</para>
///
/// <para>Registered as a <b>singleton</b> by
/// <see cref="ModuleDbContextRegistration.AddModuleDbContext{TContext}"/> — see that method for why a
/// per-scope instance would make EF rebuild its internal service provider on every scope.</para>
/// </summary>
internal sealed class AuditSaveChangesInterceptor(
    IAuditContextAccessor contextAccessor,
    TimeProvider timeProvider) : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        StageAuditEntries(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    // Both overloads, so no write path is silently unaudited. Everything in this repo saves
    // asynchronously today; a synchronous SaveChanges added later must not quietly skip the trail.
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        StageAuditEntries(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void StageAuditEntries(DbContext? context)
    {
        // Deliberately NOT a concrete-type check. The backend's interceptor tests
        // `eventData.Context is ApplicationDbContext`, which works when there is one context and
        // silently audits nothing the moment there are eight.
        if (context is null)
            return;

        var entries = BuildAuditEntries(context);
        if (entries.Count == 0)
            return;

        if (context.Model.FindEntityType(typeof(AuditLog)) is null)
        {
            // Fail closed. A context with auditable entities but no audit_logs mapping means someone
            // added the interceptor without ConfigureAudit, and the alternative to throwing is
            // dropping the trail for that whole context without a word - the precise failure shape
            // B18 taught this codebase to refuse.
            throw new InvalidOperationException(
                $"{context.GetType().Name} has {entries.Count} auditable change(s) but does not map "
                + $"{nameof(AuditLog)}. Call modelBuilder.ConfigureAudit(ownsMigration: false) in its "
                + "OnModelCreating.");
        }

        context.Set<AuditLog>().AddRange(entries);
    }

    private List<AuditLog> BuildAuditEntries(DbContext context)
    {
        var auditContext = contextAccessor.GetCurrent();
        var timestamp = timeProvider.GetUtcNow();
        var entries = new List<AuditLog>();

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            var (oldValues, newValues, changedColumns) = BuildChangeSets(entry);

            entries.Add(AuditLog.Record(
                action: MapAction(entry.State),
                entityName: entry.Metadata.ClrType.Name,
                entityId: ReadPrimaryKey(entry),
                // Per-entity schema first, so an entity deliberately mapped outside its module's
                // default schema still resolves to where it actually lives.
                module: entry.Metadata.GetSchema() ?? context.Model.GetDefaultSchema(),
                context: auditContext,
                timestamp: timestamp,
                oldValues: oldValues,
                newValues: newValues,
                changedColumns: changedColumns));
        }

        return entries;
    }

    private static (string? Old, string? New, string? Changed) BuildChangeSets(EntityEntry entry) =>
        entry.State switch
        {
            EntityState.Added => (
                null,
                Serialize(entry.Properties.ToDictionary(
                    p => p.Metadata.Name, p => AuditRedaction.Redact(p.Metadata, p.CurrentValue))),
                null),

            EntityState.Deleted => (
                Serialize(entry.Properties.ToDictionary(
                    p => p.Metadata.Name, p => AuditRedaction.Redact(p.Metadata, p.OriginalValue))),
                null,
                null),

            EntityState.Modified => BuildModifiedChangeSet(entry),

            _ => (null, null, null),
        };

    private static (string? Old, string? New, string? Changed) BuildModifiedChangeSet(EntityEntry entry)
    {
        var changed = entry.Properties.Where(p => p.IsModified).ToList();

        // An EntityState.Modified entry with no modified property is possible (a no-op setter, or a
        // navigation fix-up). Recording an audit row whose diff is two empty objects says a change
        // happened when none did.
        if (changed.Count == 0)
            return (null, null, null);

        return (
            Serialize(changed.ToDictionary(
                p => p.Metadata.Name, p => AuditRedaction.Redact(p.Metadata, p.OriginalValue))),
            Serialize(changed.ToDictionary(
                p => p.Metadata.Name, p => AuditRedaction.Redact(p.Metadata, p.CurrentValue))),
            Serialize(changed.Select(p => p.Metadata.Name)));
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static AuditAction MapAction(EntityState state) => state switch
    {
        EntityState.Added => AuditAction.Create,
        EntityState.Modified => AuditAction.Update,
        EntityState.Deleted => AuditAction.Delete,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Not an auditable entity state."),
    };

    private static string? ReadPrimaryKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null)
            return null;

        // Composite keys are joined in key-property order, which EF fixes - so the same row always
        // produces the same EntityId string and entity-history reads stay stable.
        return string.Join(',', key.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString()));
    }
}
