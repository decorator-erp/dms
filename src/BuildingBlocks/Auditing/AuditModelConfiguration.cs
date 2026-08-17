using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Auditing;

public static class AuditModelConfiguration
{
    /// <summary>
    /// Maps <see cref="AuditLog"/> onto <c>shared.audit_logs</c>. Exactly the shape
    /// <see cref="Outbox.OutboxModelConfiguration.ConfigureOutbox"/> established: every context that
    /// writes audit rows must MAP the table (so <c>ctx.Set&lt;AuditLog&gt;()</c> resolves and the row
    /// commits in the same transaction as the change), but only one context may OWN the migration that
    /// creates it. <paramref name="ownsMigration"/> is <c>true</c> in exactly one place —
    /// <see cref="Outbox.SharedDbContext"/>.
    ///
    /// <para><b>Why <c>shared</c> and not <c>notify</c>.</b> The earlier phase brief had
    /// <c>NotificationsDbContext</c> own this migration. <c>shared</c> is FIRST in
    /// <c>ModuleDependencyOrder.SchemaOrder</c>, so the table exists before any module migrates or
    /// seeds, under every host and every fixture, permanently — whereas <c>notify</c> is last, which
    /// would make correctness depend on the unstated invariant "nobody writes an audited entity
    /// between the first module migration and the last". The decisive argument is narrower:
    /// <c>tests/BuildingBlocks.IntegrationTests</c> references only BuildingBlocks and SharedKernel, so
    /// under <c>notify</c> the interceptor — a BuildingBlocks type — could not be integration-tested in
    /// the project that owns it without that project taking a dependency on a module.</para>
    ///
    /// <para><b>No foreign key on <c>user_id</c>.</b> Two independent reasons: <c>shared</c> migrates
    /// before <c>identity</c>, so the constraint is structurally impossible; and an audit row must
    /// outlive its subject — a <c>RESTRICT</c> FK would make deleting a user fail *because* that user
    /// did something auditable, which is backwards. <c>Rule9CrossSchemaForeignKeyTests</c> asserts the
    /// absence rather than leaving it to be noticed.</para>
    /// </summary>
    public static void ConfigureAudit(this ModelBuilder modelBuilder, bool ownsMigration)
    {
        modelBuilder.Entity<AuditLog>(b =>
        {
            b.ToTable("audit_logs", "shared", t =>
            {
                if (!ownsMigration)
                    t.ExcludeFromMigrations();
            });

            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

            b.Property(x => x.UserId).HasColumnName("user_id").HasMaxLength(450);
            b.Property(x => x.UserEmail).HasColumnName("user_email").HasMaxLength(256);
            b.Property(x => x.IpAddress).HasColumnName("ip_address").HasMaxLength(64);
            b.Property(x => x.UserAgent).HasColumnName("user_agent");

            b.Property(x => x.Action).HasColumnName("action").HasConversion<int>().IsRequired();
            b.Property(x => x.EntityName).HasColumnName("entity_name").HasMaxLength(128).IsRequired();
            b.Property(x => x.EntityId).HasColumnName("entity_id").HasMaxLength(256);

            b.Property(x => x.OldValues).HasColumnName("old_values").HasColumnType("jsonb");
            b.Property(x => x.NewValues).HasColumnName("new_values").HasColumnType("jsonb");
            b.Property(x => x.ChangedColumns).HasColumnName("changed_columns").HasColumnType("jsonb");

            b.Property(x => x.Module).HasColumnName("module").HasMaxLength(64);
            b.Property(x => x.Timestamp).HasColumnName("timestamp").IsRequired();

            b.Property(x => x.RequestPath).HasColumnName("request_path").HasMaxLength(512);
            b.Property(x => x.RequestMethod).HasColumnName("request_method").HasMaxLength(16);

            // Entity history: "everything that happened to this row".
            b.HasIndex(x => new { x.EntityName, x.EntityId })
                .HasDatabaseName("ix_audit_logs_entity");

            // "What did this user do", the /api/audit/my-activity and /api/audit/user/{id} reads.
            b.HasIndex(x => new { x.UserId, x.Timestamp })
                .HasDatabaseName("ix_audit_logs_user_timestamp")
                .IsDescending(false, true);

            // The unfiltered log, newest first — the default /api/audit page.
            b.HasIndex(x => x.Timestamp)
                .HasDatabaseName("ix_audit_logs_timestamp")
                .IsDescending(true);
        });
    }
}
