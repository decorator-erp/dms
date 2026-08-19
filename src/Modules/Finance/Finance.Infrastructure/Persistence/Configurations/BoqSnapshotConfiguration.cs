using BuildingBlocks;
using Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Infrastructure.Persistence.Configurations;

/// <summary>
/// Ported from <c>../backend/Infrastructure/Persistence/Configurations/BoqSnapshotConfiguration.cs</c>.
///
/// <para><b>The unique <c>(ProjectId, Version)</c> index is what makes version numbering safe.</b> The
/// next version is a fact about the set, computed by the handler; two concurrent generations would both
/// read the same maximum and both write the same number. The index turns that race into a failed insert
/// rather than two snapshots each claiming to be version 4.</para>
/// </summary>
internal sealed class BoqSnapshotConfiguration : IEntityTypeConfiguration<BoqSnapshot>
{
    public void Configure(EntityTypeBuilder<BoqSnapshot> builder)
    {
        builder.ToTable("boq_snapshots");

        builder.HasKey(x => x.Id);
        builder.ConfigureEntityConventions();

        builder.Property(x => x.Type).HasConversion<int>();
        builder.Property(x => x.Notes).HasMaxLength(512);

        builder.Property(x => x.BudgetedTotal).HasColumnType("numeric(19,4)");
        builder.Property(x => x.PurchaseTotal).HasColumnType("numeric(19,4)");
        builder.Property(x => x.DifferenceTotal).HasColumnType("numeric(19,4)");
        builder.Property(x => x.ActualTotal).HasColumnType("numeric(19,4)");

        builder.HasIndex(x => new { x.ProjectId, x.Version })
            .IsUnique()
            .HasDatabaseName("ix_boq_snapshots_project_id_version");

        builder.HasIndex(x => x.ProjectStageId).HasDatabaseName("ix_boq_snapshots_project_stage_id");
        builder.HasIndex(x => x.GeneratedById).HasDatabaseName("ix_boq_snapshots_generated_by_id");
    }
}
