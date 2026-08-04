using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Projects.Domain;

namespace Projects.Infrastructure.Persistence.Configurations;

internal sealed class ProjectStageConfiguration : IEntityTypeConfiguration<ProjectStage>
{
    public void Configure(EntityTypeBuilder<ProjectStage> builder)
    {
        builder.ToTable("project_stages");
        builder.ConfigureEntityConventions();

        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(2000);
        builder.Property(s => s.Status).HasConversion<int>();
        builder.Property(s => s.ProgressPercent).HasPrecision(5, 2);
        builder.Property(s => s.Cost).HasPrecision(18, 2);
        builder.Property(s => s.Notes).HasMaxLength(2000);

        builder.HasIndex(s => s.ProjectId);
        // CompletedById: bare Guid FK to hr.employees, raw-SQL constraint in the migration.
        builder.HasIndex(s => s.CompletedById);
    }
}
