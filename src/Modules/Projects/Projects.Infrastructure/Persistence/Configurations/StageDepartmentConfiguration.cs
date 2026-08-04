using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Projects.Domain;

namespace Projects.Infrastructure.Persistence.Configurations;

internal sealed class StageDepartmentConfiguration : IEntityTypeConfiguration<StageDepartment>
{
    public void Configure(EntityTypeBuilder<StageDepartment> builder)
    {
        builder.ToTable("stage_departments");

        // Composite key on the pair itself - StageDepartment isn't a SharedKernel.Entity (no surrogate
        // Guid Id), same shape as Hr.Domain.EmployeeDepartment.
        builder.HasKey(sd => new { sd.StageId, sd.DepartmentId });

        builder.Property(sd => sd.CreatedAtUtc).IsRequired();

        // StageId: Stage is in THIS module's own model - an ordinary same-model FK, EF emits it normally.
        builder.HasOne<Stage>().WithMany().HasForeignKey(sd => sd.StageId).OnDelete(DeleteBehavior.Cascade);

        // DepartmentId: bare Guid FK to hr.departments, raw-SQL constraint in the migration.
        builder.HasIndex(sd => sd.DepartmentId);
    }
}
