using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Projects.Domain;

namespace Projects.Infrastructure.Persistence.Configurations;

internal sealed class TaskCommentConfiguration : IEntityTypeConfiguration<TaskComment>
{
    public void Configure(EntityTypeBuilder<TaskComment> builder)
    {
        builder.ToTable("task_comments");
        builder.ConfigureEntityConventions();

        builder.Property(c => c.Content).HasMaxLength(4000).IsRequired();

        builder.HasIndex(c => c.TaskId);
        // EmployeeId: bare Guid FK to hr.employees, raw-SQL constraint in the migration.
        builder.HasIndex(c => c.EmployeeId);
    }
}
