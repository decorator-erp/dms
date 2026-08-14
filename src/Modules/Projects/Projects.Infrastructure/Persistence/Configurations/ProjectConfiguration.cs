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

        // ClientId: FK to crm.clients (G9-13, closing the Phase 2 deferral) - raw-SQL constraint in
        // the AddClientForeignKey migration (EF cannot emit it - two different models), validated in
        // application code by CreateProjectCommandHandler via Crm.Contracts.IClientDirectory before
        // Project.Create is ever called. OwnerId: bare Guid FK to hr.employees, same raw-SQL-migration
        // shape, added at G5-07. Both documented in docs/cross-schema-foreign-keys.md.
        builder.HasIndex(p => p.ClientId);
        builder.HasIndex(p => p.OwnerId);
    }
}
