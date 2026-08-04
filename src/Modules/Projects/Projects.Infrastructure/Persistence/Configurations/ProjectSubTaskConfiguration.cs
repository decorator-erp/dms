using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Projects.Domain;

namespace Projects.Infrastructure.Persistence.Configurations;

internal sealed class ProjectSubTaskConfiguration : IEntityTypeConfiguration<ProjectSubTask>
{
    public void Configure(EntityTypeBuilder<ProjectSubTask> builder)
    {
        builder.ToTable("project_sub_tasks");
        builder.ConfigureEntityConventions();

        builder.Property(s => s.Title).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(2000);

        builder.HasIndex(s => s.ProjectTaskId);
        // CompletedById: bare Guid FK to hr.employees, raw-SQL constraint in the migration.
    }
}
