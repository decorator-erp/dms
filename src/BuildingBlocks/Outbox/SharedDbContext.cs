using BuildingBlocks.Auditing;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Outbox;

/// <summary>
/// Owns the <c>shared</c> schema: <c>outbox_messages</c>, <c>inbox_messages</c> and <c>audit_logs</c>,
/// and nothing else. Every module's own DbContext also maps all three (see
/// <see cref="OutboxModelConfiguration"/> and <see cref="AuditModelConfiguration"/>) but with
/// <c>ExcludeFromMigrations()</c> — this is the only context whose migration actually creates them.
///
/// <para><c>audit_logs</c> joined at N1-02. It belongs in <c>shared</c> for the same reason the outbox
/// does — every module writes it and none owns it — plus one specific to migration order: <c>shared</c>
/// is FIRST in <c>ModuleDependencyOrder.SchemaOrder</c>, so the table exists before any module migrates
/// or seeds. See <see cref="AuditModelConfiguration.ConfigureAudit"/> for the full argument, including
/// why <c>audit_logs.user_id</c> carries no foreign key.</para>
///
/// <para><b>This context gets no audit interceptor.</b> It holds no <c>IAuditable</c> entity, so the
/// interceptor would find nothing on every save — and attaching it would add an options-hash variant
/// for no behaviour. Note it is registered directly in <c>Api.DI</c> rather than through
/// <c>AddModuleDbContext</c>, which is what keeps that true.</para>
/// </summary>
public sealed class SharedDbContext(DbContextOptions<SharedDbContext> options) : DbContext(options)
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("shared");
        modelBuilder.ConfigureOutbox(ownsMigration: true);
        modelBuilder.ConfigureAudit(ownsMigration: true);
    }
}
