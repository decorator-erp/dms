using BuildingBlocks;
using Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Infrastructure.Persistence.Configurations;

/// <summary>
/// Ported from <c>../backend/Infrastructure/Persistence/Configurations/LedgerEntryConfiguration.cs</c>.
///
/// <para><b>Nothing in this table is ever cascade-deleted.</b> The backend states the reason and it is
/// worth keeping: the ledger is append-only, corrections are reversal rows rather than edits, so a
/// cascade would delete the audit trail of a mistake along with the mistake. The two intra-schema
/// relationships declared here are both <see cref="DeleteBehavior.Restrict"/>; the cross-schema ones
/// become raw-SQL foreign keys at F1-05, also RESTRICT.</para>
///
/// <para><b><see cref="LedgerEntry.ReversalOfId"/> is a self-reference</b> — an entry pointing at the
/// entry it undoes — so it is a genuine intra-table foreign key, not a cross-module id.</para>
/// </summary>
internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("ledger_entries");

        builder.HasKey(x => x.Id);
        builder.ConfigureEntityConventions();

        builder.Property(x => x.Direction).HasConversion<int>();
        builder.Property(x => x.Type).HasConversion<int>();
        builder.Property(x => x.Method).HasConversion<int?>();

        builder.Property(x => x.Amount).HasColumnType("numeric(19,4)");

        builder.Property(x => x.VendorName).HasMaxLength(128);
        builder.Property(x => x.Reference).HasMaxLength(128);
        builder.Property(x => x.Note).HasMaxLength(512);

        builder.Property(x => x.OccurredOn).HasColumnType("date");

        // Derived from ReversalOfId, never stored: two sources of truth for "is this a reversal" is
        // exactly how an entry ends up exempt from the direction invariant without pointing at anything.
        builder.Ignore(x => x.IsReversal);

        builder.HasOne<BudgetLine>()
            .WithMany()
            .HasForeignKey(x => x.BudgetLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Variation>()
            .WithMany()
            .HasForeignKey(x => x.VariationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<LedgerEntry>()
            .WithMany()
            .HasForeignKey(x => x.ReversalOfId)
            .OnDelete(DeleteBehavior.Restrict);

        // The primary read shape: a project ledger in date order.
        builder.HasIndex(x => new { x.ProjectId, x.OccurredOn })
            .HasDatabaseName("ix_ledger_entries_project_id_occurred_on");

        builder.HasIndex(x => x.ProjectStageId).HasDatabaseName("ix_ledger_entries_project_stage_id");
        builder.HasIndex(x => x.ProjectTaskId).HasDatabaseName("ix_ledger_entries_project_task_id");
        builder.HasIndex(x => x.Type).HasDatabaseName("ix_ledger_entries_type");
        builder.HasIndex(x => x.BudgetLineId).HasDatabaseName("ix_ledger_entries_budget_line_id");
        builder.HasIndex(x => x.VariationId).HasDatabaseName("ix_ledger_entries_variation_id");
        builder.HasIndex(x => x.ReversalOfId).HasDatabaseName("ix_ledger_entries_reversal_of_id");

        // Explicit because the backend got it free from HasOne(Client) and this module has no such
        // navigation. Also required by F1-05, which indexes every cross-schema foreign key column.
        builder.HasIndex(x => x.ClientId).HasDatabaseName("ix_ledger_entries_client_id");
        builder.HasIndex(x => x.RecordedById).HasDatabaseName("ix_ledger_entries_recorded_by_id");
    }
}
