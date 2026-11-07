using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Projects.Domain;

namespace Projects.Infrastructure.Persistence.Configurations;

internal sealed class ScopeOfWorkConfiguration : IEntityTypeConfiguration<ScopeOfWork>
{
    public const string ProjectIdIndex = "IX_scopes_of_work_ProjectId";

    public void Configure(EntityTypeBuilder<ScopeOfWork> builder)
    {
        // NP4: each enum column refuses a value outside its enum (a NULL passes a CHECK).
        builder.ToTable("scopes_of_work", t => t.HasCheckConstraint("CK_scopes_of_work_Status", "\"Status\" BETWEEN 1 AND 2"));
        builder.ConfigureEntityConventions();

        builder.Property(s => s.Status).HasConversion<int>();
        builder.Property(s => s.AdjustmentAmount).HasPrecision(18, 2);
        builder.Property(s => s.AdjustmentNote).HasMaxLength(500);
        builder.Property(s => s.Notes).HasMaxLength(2000);
        builder.Ignore(s => s.Subtotal);
        builder.Ignore(s => s.Total);
        builder.Ignore(s => s.EstimatedEnd);

        // Postgres' system xmin as the optimistic-concurrency token, as on CRM leads and briefs. ScopeWrites
        // marks the root modified on every save, so an edit that only touches items is still checked.
        builder.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();

        // One scope of work per project; a second first-save racing the first trips this index
        // (ScopeWrites maps it to ConcurrentUpdate).
        builder.HasIndex(s => s.ProjectId).IsUnique().HasDatabaseName(ProjectIdIndex);
        builder.HasOne<Project>().WithMany().HasForeignKey(s => s.ProjectId).OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(s => s.Items).WithOne().HasForeignKey(i => i.ScopeOfWorkId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
