using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Projects.Domain;

namespace Projects.Infrastructure.Persistence.Configurations;

internal sealed class ProjectStageConfiguration : IEntityTypeConfiguration<ProjectStage>
{
    public void Configure(EntityTypeBuilder<ProjectStage> builder)
    {
        // NP4: each enum column refuses a value outside its enum (a NULL passes a CHECK).
        builder.ToTable("project_stages", t => t.HasCheckConstraint("CK_project_stages_Status", "\"Status\" BETWEEN 1 AND 4"));
        builder.ConfigureEntityConventions();

        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(2000);
        builder.Property(s => s.Status).HasConversion<int>();
        builder.Property(s => s.ProgressPercent).HasPrecision(5, 2);
        builder.Property(s => s.Cost).HasPrecision(18, 2);
        builder.Property(s => s.Notes).HasMaxLength(2000);

        builder.HasIndex(s => s.ProjectId);

        // P1: RESTRICT, never cascade: a project is hard-deleted only by DeleteProjectCommandHandler, which removes
        // its work first, so a delete that would orphan a row fails instead of silently taking the work with it.
        builder.HasOne<Project>().WithMany().HasForeignKey(s => s.ProjectId).OnDelete(DeleteBehavior.Restrict);
        // CompletedById: bare Guid FK to hr.employees, raw-SQL constraint in the migration.
        builder.HasIndex(s => s.CompletedById);
    }
}
