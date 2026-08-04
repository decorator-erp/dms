using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Projects.Domain;

namespace Projects.Infrastructure.Persistence.Configurations;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.ConfigureEntityConventions();

        builder.Property(p => p.Code).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.Status).HasConversion<int>();
        builder.Property(p => p.WorkflowType).HasConversion<int>();
        builder.Property(p => p.Address).HasMaxLength(500).IsRequired();
        builder.Property(p => p.Notes).HasMaxLength(2000);
        builder.Property(p => p.ProgressPercentage).HasPrecision(5, 2);

        builder.HasIndex(p => p.Code).IsUnique();

        // ClientId: no FK - CRM absent, unvalidated Guid (documented in the migration +
        // docs/cross-schema-foreign-keys.md). OwnerId: bare Guid FK to hr.employees, raw-SQL constraint
        // added in the migration (EF cannot emit it - two different models).
        builder.HasIndex(p => p.OwnerId);
    }
}
