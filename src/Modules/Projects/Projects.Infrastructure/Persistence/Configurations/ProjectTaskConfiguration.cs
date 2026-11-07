using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Projects.Domain;

namespace Projects.Infrastructure.Persistence.Configurations;

internal sealed class ProjectTaskConfiguration : IEntityTypeConfiguration<ProjectTask>
{
    public void Configure(EntityTypeBuilder<ProjectTask> builder)
    {
        builder.ToTable("project_tasks");
        builder.ConfigureEntityConventions();

        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(2000);
        builder.Property(t => t.Status).HasConversion<int>();
        builder.Property(t => t.Priority).HasConversion<int>();
        builder.Property(t => t.Weight).HasPrecision(9, 2);
        builder.Property(t => t.Notes).HasMaxLength(2000);
        builder.Property(t => t.Cost).HasPrecision(18, 2);

        builder.HasIndex(t => t.ProjectId);

        // P1: RESTRICT, never cascade: a project is hard-deleted only by DeleteProjectCommandHandler, which removes
        // its work first, so a delete that would orphan a row fails instead of silently taking the work with it.
        builder.HasOne<Project>().WithMany().HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => t.ProjectStageId);
        // AssignedToId/DepartmentId: bare Guid FKs to hr.employees/hr.departments, raw-SQL constraints
        // in the migration.
        builder.HasIndex(t => t.AssignedToId);
        builder.HasIndex(t => t.DepartmentId);
    }
}
