using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Projects.Domain;

namespace Projects.Infrastructure.Persistence.Configurations;

internal sealed class StageTaskConfiguration : IEntityTypeConfiguration<StageTask>
{
    public void Configure(EntityTypeBuilder<StageTask> builder)
    {
        builder.ToTable("stage_tasks");
        builder.ConfigureEntityConventions();

        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(2000);
        builder.Property(t => t.Weight).HasPrecision(9, 2);

        builder.HasIndex(t => t.StageId);
        // DepartmentId: bare Guid FK to hr.departments, raw-SQL constraint in the migration.
        builder.HasIndex(t => t.DepartmentId);
    }
}
