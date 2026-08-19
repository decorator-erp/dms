using BuildingBlocks;
using Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Infrastructure.Persistence.Configurations;

/// <summary>
/// Ported from <c>../backend/Infrastructure/Persistence/Configurations/BoqSnapshotLineConfiguration.cs</c>.
///
/// <para><b>There is no kind column, deliberately.</b> The kind of a line is derived from which source
/// id is set, in a fixed order (<c>VariationId</c> wins over <c>InvoiceLineId</c>) — see
/// <c>BoqSnapshotLine.Kind</c>. Persisting it would create a second source of truth able to disagree
/// with the ids it is supposed to describe, on rows that are meant to be immutable.</para>
///
/// <para>Lines cascade from their snapshot, which is a single immutable statement whose lines have no
/// separate existence. The two source-id relationships are RESTRICT instead: a snapshot cites the
/// invoice line and the variation it was computed from, and deleting either would leave a historical
/// total that can no longer be explained.</para>
/// </summary>
internal sealed class BoqSnapshotLineConfiguration : IEntityTypeConfiguration<BoqSnapshotLine>
{
    public void Configure(EntityTypeBuilder<BoqSnapshotLine> builder)
    {
        builder.ToTable("boq_snapshot_lines");

        builder.HasKey(x => x.Id);
        builder.ConfigureEntityConventions();

        builder.Property(x => x.Description).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Unit).HasMaxLength(16);

        builder.Property(x => x.Quantity).HasColumnType("numeric(19,4)");
        builder.Property(x => x.BudgetedAmount).HasColumnType("numeric(19,4)");
        builder.Property(x => x.ActualAmount).HasColumnType("numeric(19,4)");
        builder.Property(x => x.DifferenceAmount).HasColumnType("numeric(19,4)");

        builder.Ignore(x => x.Kind);

        builder.HasOne<BoqSnapshot>()
            .WithMany()
            .HasForeignKey(x => x.BoqSnapshotId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<InvoiceLine>()
            .WithMany()
            .HasForeignKey(x => x.InvoiceLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Variation>()
            .WithMany()
            .HasForeignKey(x => x.VariationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.BoqSnapshotId).HasDatabaseName("ix_boq_snapshot_lines_boq_snapshot_id");
        builder.HasIndex(x => x.InvoiceLineId).HasDatabaseName("ix_boq_snapshot_lines_invoice_line_id");
        builder.HasIndex(x => x.VariationId).HasDatabaseName("ix_boq_snapshot_lines_variation_id");
        builder.HasIndex(x => x.ProjectTaskId).HasDatabaseName("ix_boq_snapshot_lines_project_task_id");
    }
}
